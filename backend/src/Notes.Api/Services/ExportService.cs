using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notes.Api.Data;
using Notes.Api.Infrastructure;

namespace Notes.Api.Services;

/// <summary>
/// Exports all of a user's notes as a ZIP of Markdown files, so the data can move to another app
/// (Obsidian, any app that imports Markdown with front matter, any editor). Notebooks become folders, each
/// note a <c>.md</c> file (<c>.html</c> for HTML notes) with YAML front matter (id, dates, tags,
/// to-do state), and attachments go to <c>_resources/</c>. Links <c>:/&lt;id&gt;</c> to notes and
/// attachments are rewritten to relative paths. The trash and version history are not exported.
/// </summary>
public partial class ExportService(NotesDbContext db, ResourceService resources, IOptions<NotesOptions> options)
{
    /// <summary>
    /// Builds the export in a temporary file next to the attachments (the container's /tmp is small
    /// and in memory) and returns it open for reading; the file is deleted when the stream is closed.
    /// </summary>
    public async Task<Stream> CreateAsync(Guid userId, CancellationToken ct = default)
    {
        var directory = TempDirectory(options.Value);
        Directory.CreateDirectory(directory);
        var file = new FileStream(Path.Combine(directory, $"export-{Guid.NewGuid():N}.zip"), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await WriteAsync(userId, file, ct);
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    /// <summary>Removes exports left over from a crash (called on startup).</summary>
    public static void DeleteLeftovers(NotesOptions options)
    {
        var directory = TempDirectory(options);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    /// <summary>Temporary files of exports and imports, in the data directory.</summary>
    internal static string TempDirectory(NotesOptions options) => Path.Combine(Path.GetFullPath(options.DataDirectory), "tmp");

    /// <summary>Writes the export ZIP to <paramref name="output"/>.</summary>
    internal async Task WriteAsync(Guid userId, Stream output, CancellationToken ct = default)
    {
        var notebooks = await db.Notebooks.AsNoTracking().Where(n => n.UserId == userId && n.DeletedTime == null).ToListAsync(ct);
        var notes = await db.Notes.AsNoTracking()
            .Where(n => n.UserId == userId && n.DeletedTime == null)
            .Include(n => n.NoteTags).ThenInclude(nt => nt.Tag)
            .OrderBy(n => n.Title).ThenBy(n => n.CreatedTime).ThenBy(n => n.Id)
            .ToListAsync(ct);
        var attachments = await db.Resources.AsNoTracking().Where(r => r.UserId == userId).OrderBy(r => r.CreatedTime).ToListAsync(ct);

        // Folder of every notebook, from its title and its parents' (unique among siblings)
        var byId = notebooks.ToDictionary(n => n.Id);
        var folderNames = new Dictionary<Guid, string>();
        foreach (var group in notebooks.GroupBy(n => n.ParentId))
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "_resources" };
            foreach (var notebook in group.OrderBy(n => n.Title).ThenBy(n => n.CreatedTime).ThenBy(n => n.Id))
            {
                folderNames[notebook.Id] = Unique(SafeName(notebook.Title, "Notebook"), "", taken);
            }
        }
        string FolderOf(Guid notebookId)
        {
            var parts = new List<string>();
            for (Guid? id = notebookId; id is { } current && byId.TryGetValue(current, out var notebook) && parts.Count < 64; id = notebook.ParentId)
            {
                parts.Insert(0, folderNames[current]);
            }
            return string.Join('/', parts);
        }

        // Every exported file's path, so :/id links can point at it
        var paths = new Dictionary<Guid, string>();
        var takenInFolder = new Dictionary<string, HashSet<string>>();
        foreach (var note in notes)
        {
            if (!byId.ContainsKey(note.NotebookId)) continue;
            var folder = FolderOf(note.NotebookId);
            var taken = takenInFolder.TryGetValue(folder, out var set) ? set : takenInFolder[folder] = new(StringComparer.OrdinalIgnoreCase);
            var extension = note.MarkupLanguage == TextPreview.MarkupHtml ? ".html" : ".md";
            paths[note.Id] = $"{folder}/{Unique(SafeName(note.Title, "Untitled"), extension, taken)}";
        }
        var resourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in attachments)
        {
            var dot = resource.FileName.LastIndexOf('.');
            var (stem, extension) = dot > 0 ? (resource.FileName[..dot], resource.FileName[dot..]) : (resource.FileName, "");
            paths[resource.Id] = "_resources/" + Unique(SafeName(stem, "file"), SafeName(extension, ""), resourceNames);
        }

        await using var zip = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8, ct);
        foreach (var note in notes)
        {
            if (!paths.TryGetValue(note.Id, out var path)) continue;
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            entry.LastWriteTime = note.UpdatedTime;
            await using var stream = await entry.OpenAsync(ct);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(FrontMatter(note));
            await writer.WriteAsync(RewriteLinks(note.Body, path, paths));
        }
        foreach (var resource in attachments)
        {
            await using var file = resources.TryOpenFile(userId, resource.Id);
            if (file is null) continue;
            var entry = zip.CreateEntry(paths[resource.Id], CompressionLevel.Optimal);
            entry.LastWriteTime = resource.CreatedTime;
            await using var stream = await entry.OpenAsync(ct);
            await file.CopyToAsync(stream, ct);
        }
    }

    private static string FrontMatter(Note note)
    {
        var yaml = new StringBuilder("---\n");
        yaml.Append("title: ").Append(Quote(note.Title)).Append('\n');
        yaml.Append("id: ").Append(Ids.Format(note.Id)).Append('\n');
        yaml.Append("created: ").Append(Iso(note.CreatedTime)).Append('\n');
        yaml.Append("updated: ").Append(Iso(note.UpdatedTime)).Append('\n');
        var tags = note.NoteTags.Select(nt => nt.Tag?.Title).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (tags.Count > 0)
        {
            yaml.Append("tags:\n");
            foreach (var tag in tags) yaml.Append("  - ").Append(Quote(tag)).Append('\n');
        }
        if (note.IsTodo)
        {
            yaml.Append("completed?: ").Append(note.TodoCompletedTime is null ? "no" : "yes").Append('\n');
            if (note.TodoDueTime is { } due) yaml.Append("due: ").Append(Iso(due)).Append('\n');
        }
        return yaml.Append("---\n\n").ToString();
    }

    /// <summary>Replaces <c>:/id</c> links to exported notes and attachments with paths relative to the note.</summary>
    internal static string RewriteLinks(string body, string notePath, IReadOnlyDictionary<Guid, string> paths)
    {
        var up = string.Concat(Enumerable.Repeat("../", notePath.Count(c => c == '/')));
        return InternalLink().Replace(body, match =>
            Guid.TryParseExact(match.Groups[1].Value, "N", out var id) && paths.TryGetValue(id, out var target)
                ? up + string.Join('/', target.Split('/').Select(Uri.EscapeDataString))
                : match.Value);
    }

    /// <summary>A file or folder name that is valid on Windows, macOS and Linux.</summary>
    internal static string SafeName(string? title, string fallback)
    {
        var cleaned = UnsafeChars().Replace(title ?? "", "_").Trim().TrimEnd('.');
        if (cleaned.Length > 100) cleaned = cleaned[..100].TrimEnd();
        if (ReservedNames().IsMatch(cleaned)) cleaned = "_" + cleaned;
        return cleaned.Length > 0 ? cleaned : fallback;
    }

    private static string Unique(string name, string extension, HashSet<string> taken)
    {
        var candidate = name + extension;
        for (var i = 2; !taken.Add(candidate); i++) candidate = $"{name} ({i}){extension}";
        return candidate;
    }

    private static string Iso(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "") + "\"";

    [GeneratedRegex(@"(?<![\w/]):/([0-9a-fA-F]{32})\b")]
    private static partial Regex InternalLink();

    [GeneratedRegex(@"[\x00-\x1f<>:""/\\|?*]")]
    private static partial Regex UnsafeChars();

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\..*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReservedNames();
}
