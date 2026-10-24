using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notes.Api.Data;
using Notes.Api.Infrastructure;

namespace Notes.Api.Services;

/// <summary>
/// Imports a ZIP of Markdown files, the format <see cref="ExportService"/> writes (Obsidian and other
/// tools produce something close). Everything goes into a new top-level notebook "Import &lt;date&gt;":
/// folders become notebooks, <c>.md</c>/<c>.markdown</c> files Markdown notes and <c>.html</c> files HTML
/// notes, with title, dates, tags and to-do state from the YAML front matter. All other files become
/// attachments, and relative links to them or to other imported notes are rewritten to <c>:/&lt;id&gt;</c>.
/// The archive is never unpacked to disk: entry names only name notebooks and resolve links. Entry
/// count, entry sizes and the total unpacked size are limited, so a ZIP bomb is refused.
/// </summary>
public partial class ImportService(NotesDbContext db, ResourceService resources, TimeProvider clock, IOptions<NotesOptions> options)
{
    /// <summary>Most entries an archive may have.</summary>
    public const int MaxEntries = 20_000;

    /// <summary>The unpacked archive may be at most this many times <see cref="NotesOptions.MaxImportMb"/>.</summary>
    public const int MaxExpansion = 10;

    /// <summary>At most this many skipped entries are listed in the result.</summary>
    private const int MaxReportedSkips = 100;

    private const long Mb = 1024 * 1024;

    private static readonly string[] NoteExtensions = [".md", ".markdown", ".html"];

    /// <summary>
    /// Imports the ZIP in <paramref name="body"/> (at most <see cref="NotesOptions.MaxImportMb"/>). It is
    /// buffered in a temporary file in the data directory, since a ZIP is read from its end.
    /// </summary>
    public async Task<ImportResultDto> ImportAsync(Guid userId, Stream body, CancellationToken ct = default)
    {
        var directory = ExportService.TempDirectory(options.Value);
        Directory.CreateDirectory(directory);
        await using var file = new FileStream(Path.Combine(directory, $"import-{Guid.NewGuid():N}.zip"), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        var maxBytes = options.Value.MaxImportMb * Mb;
        await using (var limited = new LimitedStream(body, maxBytes, () => TooLarge($"The file is larger than {options.Value.MaxImportMb} MB")))
        {
            await limited.CopyToAsync(file, ct);
        }
        if (file.Length == 0) throw ApiException.BadRequest("No file content received");
        file.Position = 0;

        ZipArchive zip;
        try
        {
            zip = await ZipArchive.CreateAsync(file, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: Encoding.UTF8, ct);
        }
        catch (InvalidDataException)
        {
            throw ApiException.BadRequest("This is not a ZIP file");
        }
        await using (zip)
        {
            try
            {
                return await ImportAsync(userId, zip, ct);
            }
            catch (InvalidDataException)
            {
                throw ApiException.BadRequest("The ZIP file is damaged");
            }
        }
    }

    private async Task<ImportResultDto> ImportAsync(Guid userId, ZipArchive zip, CancellationToken ct)
    {
        if (zip.Entries.Count > MaxEntries) throw TooLarge($"The ZIP file has more than {MaxEntries} entries");
        var maxUnpacked = options.Value.MaxImportMb * Mb * MaxExpansion;
        if (zip.Entries.Sum(e => e.Length) > maxUnpacked) throw TooLarge($"The ZIP file unpacks to more than {maxUnpacked / Mb} MB");
        var maxEntryBytes = options.Value.MaxUploadMb * Mb;

        var skipped = new List<ImportSkipDto>();
        var notes = new List<(string Path, ZipArchiveEntry Entry)>();
        var files = new List<(string Path, ZipArchiveEntry Entry)>();
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            var path = NormalizePath(entry.FullName);
            if (path is null)
            {
                skipped.Add(new(entry.FullName, "unsafe path"));
                continue;
            }
            // macOS metadata and hidden files
            if (path.Split('/').Any(segment => segment.StartsWith('.') || segment == "__MACOSX")) continue;
            if (entry.Length > maxEntryBytes)
            {
                skipped.Add(new(path, $"larger than {options.Value.MaxUploadMb} MB"));
                continue;
            }
            (IsNote(path) ? notes : files).Add((path, entry));
        }

        // Ids of everything that can be linked, by path, before any body is rewritten
        var targets = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var (path, _) in notes) targets[path] = Guid.NewGuid();

        var created = new List<Guid>();
        try
        {
            foreach (var (path, entry) in files)
            {
                await using var content = await OpenAsync(entry, ct);
                try
                {
                    var resource = await resources.CreateAsync(userId, path[(path.LastIndexOf('/') + 1)..], null, content, ct);
                    var id = Ids.Parse(resource.Id, "resource id");
                    created.Add(id);
                    targets[path] = id;
                }
                catch (ApiException error) when (error.Status == StatusCodes.Status400BadRequest)
                {
                    skipped.Add(new(path, error.Message));
                }
            }
            var result = await CreateNotesAsync(userId, notes, targets, ct);
            return result with { Attachments = created.Count, Skipped = skipped.Take(MaxReportedSkips).ToList(), SkippedCount = skipped.Count };
        }
        catch
        {
            // Nothing half-imported stays behind
            foreach (var id in created) await resources.DeleteAsync(userId, Ids.Format(id), CancellationToken.None);
            throw;
        }
    }

    private async Task<ImportResultDto> CreateNotesAsync(Guid userId, List<(string Path, ZipArchiveEntry Entry)> notes,
        Dictionary<string, Guid> targets, CancellationToken ct)
    {
        var now = Times.Now(clock);
        var root = new Notebook
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = $"Import {now.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}",
            CreatedTime = now,
            UpdatedTime = now,
        };
        db.Notebooks.Add(root);
        var folders = new Dictionary<string, Guid>(StringComparer.Ordinal) { [""] = root.Id };
        Guid FolderId(string folder)
        {
            if (folders.TryGetValue(folder, out var id)) return id;
            var slash = folder.LastIndexOf('/');
            var parent = FolderId(slash < 0 ? "" : folder[..slash]);
            var notebook = new Notebook
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ParentId = parent,
                Title = Titles.Clean(folder[(slash + 1)..]),
                CreatedTime = now,
                UpdatedTime = now,
            };
            db.Notebooks.Add(notebook);
            return folders[folder] = notebook.Id;
        }

        var tags = await db.Tags.Where(t => t.UserId == userId).ToDictionaryAsync(t => t.Title.ToLowerInvariant(), ct);
        foreach (var (path, entry) in notes.OrderBy(n => n.Path, StringComparer.Ordinal))
        {
            string text;
            await using (var content = await OpenAsync(entry, ct))
            using (var reader = new StreamReader(content, Encoding.UTF8))
            {
                text = await reader.ReadToEndAsync(ct);
            }
            var (front, body) = FrontMatter.Parse(text);
            var slash = path.LastIndexOf('/');
            var folder = slash < 0 ? "" : path[..slash];
            var fileName = path[(slash + 1)..];
            var title = Titles.Clean(front.Title ?? fileName[..fileName.LastIndexOf('.')]);
            var markup = path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? TextPreview.MarkupHtml : 1;
            var created = (front.Created ?? entry.LastWriteTime).ToUniversalTime();
            var note = new Note
            {
                Id = targets[path],
                UserId = userId,
                NotebookId = FolderId(folder),
                Title = title,
                Body = RewriteLinks(body, folder, targets),
                MarkupLanguage = markup,
                IsTodo = front.Completed is not null,
                TodoCompletedTime = front.Completed == true ? front.Updated?.ToUniversalTime() ?? created : null,
                TodoDueTime = front.Completed is not null ? front.Due?.ToUniversalTime() : null,
                CreatedTime = created,
                UpdatedTime = front.Updated?.ToUniversalTime() ?? created,
            };
            note.Preview = TextPreview.For(note.Body, note.MarkupLanguage);
            foreach (var tagTitle in front.Tags.Select(Titles.Clean).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!tags.TryGetValue(tagTitle.ToLowerInvariant(), out var tag))
                {
                    tag = new Tag { Id = Guid.NewGuid(), UserId = userId, Title = tagTitle, CreatedTime = now, UpdatedTime = now };
                    db.Tags.Add(tag);
                    tags[tagTitle.ToLowerInvariant()] = tag;
                }
                note.NoteTags.Add(new NoteTag { TagId = tag.Id, CreatedTime = now });
            }
            db.Notes.Add(note);
        }

        await db.SaveChangesAsync(ct);
        return new ImportResultDto(Ids.Format(root.Id), root.Title, notes.Count, folders.Count - 1, 0, [], 0);
    }

    /// <summary>An entry's content, never more than it declares (so its size checks hold).</summary>
    private static async Task<Stream> OpenAsync(ZipArchiveEntry entry, CancellationToken ct) =>
        new LimitedStream(await entry.OpenAsync(ct), entry.Length, () => new InvalidDataException("Entry is longer than declared"));

    private static bool IsNote(string path) => NoteExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private static ApiException TooLarge(string message) => new(StatusCodes.Status413PayloadTooLarge, message);

    /// <summary>
    /// An entry name as a clean relative path with <c>/</c> separators, or null when it is absolute,
    /// climbs out with <c>..</c> or contains control characters.
    /// </summary>
    internal static string? NormalizePath(string name)
    {
        var segments = name.Replace('\\', '/').Split('/');
        if (segments[0].Length == 0 || segments[0].Contains(':')) return null;
        var clean = new List<string>();
        foreach (var segment in segments)
        {
            if (segment is "" or ".") continue;
            if (segment == ".." || segment.Any(char.IsControl)) return null;
            clean.Add(segment);
        }
        return clean.Count > 0 ? string.Join('/', clean) : null;
    }

    /// <summary>
    /// Rewrites relative Markdown links and images (<c>](path)</c>) and HTML <c>src</c>/<c>href</c> that point
    /// at imported files to <c>:/id</c>; anything else (web links, unknown paths) stays as it is.
    /// </summary>
    internal static string RewriteLinks(string body, string folder, IReadOnlyDictionary<string, Guid> targets)
    {
        string? LinkTo(string target)
        {
            var hash = target.IndexOf('#');
            var (path, fragment) = hash >= 0 ? (target[..hash], target[hash..]) : (target, "");
            if (path.Length == 0 || path.Contains(':') || path.StartsWith('/')) return null;
            try
            {
                path = Uri.UnescapeDataString(path);
            }
            catch (UriFormatException)
            {
                return null;
            }
            var resolved = Resolve(folder, path);
            return resolved is not null && targets.TryGetValue(resolved, out var id) ? $":/{Ids.Format(id)}{fragment}" : null;
        }

        body = MarkdownLink().Replace(body, m => LinkTo(m.Groups[2].Value) is { } link ? $"{m.Groups[1].Value}{link}{m.Groups[3].Value}" : m.Value);
        return HtmlLink().Replace(body, m => LinkTo(m.Groups[2].Value) is { } link ? $"{m.Groups[1].Value}{link}\"" : m.Value);
    }

    /// <summary>A relative link from a note in <paramref name="folder"/> as an archive path; null when it leaves the archive.</summary>
    private static string? Resolve(string folder, string path)
    {
        var parts = folder.Length == 0 ? new List<string>() : folder.Split('/').ToList();
        foreach (var segment in path.Replace('\\', '/').Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (parts.Count == 0) return null;
                parts.RemoveAt(parts.Count - 1);
            }
            else
            {
                parts.Add(segment);
            }
        }
        return parts.Count > 0 ? NormalizePath(string.Join('/', parts)) : null;
    }

    [GeneratedRegex(@"(\]\(\s*<?)([^)\s<>]+)(>?(?:\s+""[^""]*"")?\s*\))")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"(\b(?:src|href)\s*=\s*"")([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlLink();

    /// <summary>Reads at most <c>limit</c> bytes; one more throws the exception from <c>tooLong</c>.</summary>
    private sealed class LimitedStream(Stream inner, long limit, Func<Exception> tooLong) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => _read; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private int Count(int read)
        {
            _read += read;
            if (_read > limit) throw tooLong();
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}

/// <summary>What an import created; <c>Skipped</c> lists up to 100 entries that were left out, and why.</summary>
public record ImportResultDto(string NotebookId, string NotebookTitle, int Notes, int Notebooks, int Attachments, List<ImportSkipDto> Skipped, int SkippedCount);

public record ImportSkipDto(string Path, string Reason);
