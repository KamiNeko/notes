using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notes.Api.Data;
using Notes.Api.Infrastructure;

namespace Notes.Api.Services;

/// <summary>
/// Version history of notes. Before a title/body change the previous content is saved as a version,
/// unless the last version was taken less than <see cref="NotesOptions.VersionIntervalMinutes"/> ago:
/// an editing session with many autosaves leaves one version (its starting point), not one per save.
/// </summary>
public class VersionService(NotesDbContext db, IOptions<NotesOptions> options)
{
    /// <summary>
    /// Adds the note's current content as a version when the interval since the last one has passed
    /// (or always, with <paramref name="force"/>). Empty notes and content equal to the last version are skipped.
    /// The caller saves the context.
    /// </summary>
    public async Task SnapshotBeforeChangeAsync(Note note, DateTimeOffset now, CancellationToken ct, bool force = false)
    {
        if (note.Title.Length == 0 && note.Body.Length == 0) return;
        var last = await db.NoteVersions.AsNoTracking()
            .Where(v => v.NoteId == note.Id)
            .OrderByDescending(v => v.CreatedTime)
            .Select(v => new { v.CreatedTime, v.Title, v.Body })
            .FirstOrDefaultAsync(ct);
        if (last is not null)
        {
            if (last.Title == note.Title && last.Body == note.Body) return;
            var interval = TimeSpan.FromMinutes(Math.Max(0, options.Value.VersionIntervalMinutes));
            if (!force && now - last.CreatedTime < interval) return;
        }
        db.NoteVersions.Add(new NoteVersion
        {
            Id = Guid.NewGuid(),
            NoteId = note.Id,
            UserId = note.UserId,
            Title = note.Title,
            Body = note.Body,
            NoteUpdatedTime = note.UpdatedTime,
            CreatedTime = now,
        });
    }

    /// <summary>The note's versions, newest first, without bodies.</summary>
    public async Task<List<NoteVersionDto>> ListAsync(Guid userId, Guid noteId, CancellationToken ct = default)
    {
        var rows = await db.NoteVersions.AsNoTracking()
            .Where(v => v.NoteId == noteId && v.UserId == userId)
            .OrderByDescending(v => v.CreatedTime)
            .Select(v => new { v.Id, v.Title, Start = v.Body.Substring(0, 600), v.NoteUpdatedTime, v.CreatedTime })
            .ToListAsync(ct);
        return rows.Select(v => new NoteVersionDto(Ids.Format(v.Id), Ids.Format(noteId), v.Title,
            TextPreview.StripMarkdown(v.Start), Times.ToMs(v.NoteUpdatedTime), Times.ToMs(v.CreatedTime))).ToList();
    }

    /// <summary>One version with its body, or 404.</summary>
    public async Task<NoteVersion> FindAsync(Guid userId, Guid noteId, string versionId, CancellationToken ct = default)
    {
        var id = Ids.Parse(versionId, "version id");
        return await db.NoteVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id && v.NoteId == noteId && v.UserId == userId, ct)
            ?? throw ApiException.NotFound($"Version not found: {versionId}");
    }

    /// <summary>Deletes versions older than the retention period (when one is configured).</summary>
    public async Task<int> PruneAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var days = options.Value.VersionRetentionDays;
        if (days <= 0) return 0;
        var cutoff = now - TimeSpan.FromDays(days);
        return await db.NoteVersions.Where(v => v.CreatedTime < cutoff).ExecuteDeleteAsync(ct);
    }

    public static NoteVersionDetailDto ToDetail(NoteVersion v) =>
        new(Ids.Format(v.Id), Ids.Format(v.NoteId), v.Title, v.Body, Times.ToMs(v.NoteUpdatedTime), Times.ToMs(v.CreatedTime));
}
