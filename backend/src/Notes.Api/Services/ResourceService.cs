using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notes.Api.Data;
using Notes.Api.Infrastructure;

namespace Notes.Api.Services;

/// <summary>
/// Attachments. Metadata lives in Postgres, the files under
/// <c>&lt;DataDirectory&gt;/attachments/&lt;userId&gt;/&lt;resourceId&gt;</c>. Notes link them as <c>:/&lt;id&gt;</c>.
/// </summary>
public class ResourceService(NotesDbContext db, TimeProvider clock, IOptions<NotesOptions> options)
{
    private const long Mb = 1024 * 1024;

    private string UserDirectory(Guid userId) =>
        Path.Combine(Path.GetFullPath(options.Value.DataDirectory), "attachments", Ids.Format(userId));

    private string FilePath(Guid userId, Guid resourceId) => Path.Combine(UserDirectory(userId), Ids.Format(resourceId));

    /// <summary>How much space the user's attachments take, and their limit.</summary>
    public async Task<StorageDto> StorageAsync(Guid userId, CancellationToken ct = default) =>
        new(await UsedBytesAsync(userId, ct), await QuotaBytesAsync(userId, ct));

    private Task<long> UsedBytesAsync(Guid userId, CancellationToken ct) =>
        db.Resources.Where(r => r.UserId == userId).SumAsync(r => r.Size, ct);

    /// <summary>The user's own limit or else <c>Notes__QuotaMb</c>, in bytes; null when unlimited.</summary>
    private async Task<long?> QuotaBytesAsync(Guid userId, CancellationToken ct)
    {
        var own = await db.Users.Where(u => u.Id == userId).Select(u => u.QuotaMb).FirstOrDefaultAsync(ct);
        var mb = own ?? options.Value.QuotaMb;
        return mb > 0 ? mb * Mb : null;
    }

    private static ApiException StorageFull(long used, long quota) => new(
        StatusCodes.Status413PayloadTooLarge,
        $"Not enough storage space: {FormatMb(used)} of {FormatMb(quota)} used. Delete attachments you no longer need or ask an administrator for more space.",
        new { usedBytes = used, quotaBytes = quota });

    private static string FormatMb(long bytes) => (bytes / (double)Mb).ToString("0.#", CultureInfo.InvariantCulture) + " MB";

    /// <summary>
    /// Stores an uploaded file. The MIME type is taken from the browser, the extension or the content.
    /// Uploads past the user's storage limit get 413.
    /// </summary>
    public async Task<ResourceDto> CreateAsync(Guid userId, string fileName, string? declaredType, Stream content, CancellationToken ct = default)
    {
        var quota = await QuotaBytesAsync(userId, ct);
        if (quota is { } full)
        {
            // A full account is refused before the file is received
            var usedBefore = await UsedBytesAsync(userId, ct);
            if (usedBefore >= full) throw StorageFull(usedBefore, full);
        }
        var resource = new Resource
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FileName = ResourceTypes.CleanFileName(fileName),
            CreatedTime = Times.Now(clock),
        };
        Directory.CreateDirectory(UserDirectory(userId));
        var path = FilePath(userId, resource.Id);
        var temp = path + ".upload";
        var head = new byte[512];
        int headLength;
        try
        {
            await using (var file = File.Create(temp))
            {
                await content.CopyToAsync(file, ct);
                resource.Size = file.Length;
                file.Position = 0;
                headLength = await file.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
            }
            if (resource.Size == 0) throw ApiException.BadRequest("No file content received");
            resource.MimeType = ResourceTypes.ChooseMimeType(declaredType, resource.FileName, head.AsSpan(0, headLength));
            File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (quota is { } limit)
            {
                // One upload per user at a time from here, so parallel uploads can't pass the limit together
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({QuotaLockKey(userId)})", ct);
                var used = await UsedBytesAsync(userId, ct);
                if (used + resource.Size > limit) throw StorageFull(used, limit);
            }
            db.Resources.Add(resource);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
        return new ResourceDto(Ids.Format(resource.Id), resource.FileName, resource.MimeType, resource.Size);
    }

    private static long QuotaLockKey(Guid userId) => BitConverter.ToInt64(userId.ToByteArray());

    /// <summary>The stored file of an attachment, or null when it is missing (for the export).</summary>
    internal Stream? TryOpenFile(Guid userId, Guid resourceId)
    {
        var path = FilePath(userId, resourceId);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    /// <summary>An attachment's metadata and an open stream of its file, or 404.</summary>
    public async Task<(Resource Resource, Stream Content)> OpenAsync(Guid userId, string id, CancellationToken ct = default)
    {
        var resourceId = Ids.Parse(id, "resource id");
        var resource = await db.Resources.AsNoTracking().FirstOrDefaultAsync(r => r.Id == resourceId && r.UserId == userId, ct)
            ?? throw ApiException.NotFound("Resource not found");
        var path = FilePath(userId, resourceId);
        if (!File.Exists(path)) throw ApiException.NotFound("Resource not found");
        return (resource, File.OpenRead(path));
    }

    /// <summary>Deletes an attachment (metadata and file). Notes linking to it are not changed.</summary>
    public async Task DeleteAsync(Guid userId, string id, CancellationToken ct = default)
    {
        var resourceId = Ids.Parse(id, "resource id");
        var deleted = await db.Resources.Where(r => r.Id == resourceId && r.UserId == userId).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw ApiException.NotFound("Resource not found");
        var path = FilePath(userId, resourceId);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>
    /// Deletes attachments older than <see cref="NotesOptions.AttachmentCleanupDays"/> that no note
    /// (also in the trash) and no version links to, and attachment files without a row (left by a
    /// crash between file and row write, or <c>.upload</c> leftovers). The grace period keeps an
    /// upload whose note isn't saved yet. Returns the number of attachments and stray files deleted.
    /// </summary>
    public async Task<(int Attachments, int Files)> DeleteUnusedAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var days = options.Value.AttachmentCleanupDays;
        if (days <= 0) return (0, 0);
        var cutoff = now - TimeSpan.FromDays(days);

        var candidates = await db.Resources.AsNoTracking()
            .Where(r => r.CreatedTime < cutoff)
            .Select(r => new { r.Id, r.UserId })
            .ToListAsync(ct);
        var attachments = 0;
        foreach (var candidate in candidates)
        {
            var link = ":/" + Ids.Format(candidate.Id);
            var linked = await db.Notes.AnyAsync(n => n.UserId == candidate.UserId && n.Body.Contains(link), ct)
                || await db.NoteVersions.AnyAsync(v => v.UserId == candidate.UserId && v.Body.Contains(link), ct);
            if (linked) continue;
            attachments += await db.Resources.Where(r => r.Id == candidate.Id).ExecuteDeleteAsync(ct);
            var path = FilePath(candidate.UserId, candidate.Id);
            if (File.Exists(path)) File.Delete(path);
        }

        var files = 0;
        var root = Path.Combine(Path.GetFullPath(options.Value.DataDirectory), "attachments");
        if (!Directory.Exists(root)) return (attachments, files);
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (!Ids.TryParse(Path.GetFileName(directory), out var userId)) continue;
            var known = (await db.Resources.AsNoTracking().Where(r => r.UserId == userId).Select(r => r.Id).ToListAsync(ct)).ToHashSet();
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (Ids.TryParse(Path.GetFileName(file), out var id) && known.Contains(id.Value)) continue;
                if (File.GetLastWriteTimeUtc(file) >= cutoff.UtcDateTime) continue;
                File.Delete(file);
                files++;
            }
        }
        return (attachments, files);
    }

    /// <summary>Deletes all attachment files of a user (their rows go with the user).</summary>
    public void DeleteAllFiles(Guid userId)
    {
        var directory = UserDirectory(userId);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
