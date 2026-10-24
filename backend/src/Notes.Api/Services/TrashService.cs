using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notes.Api.Data;
using Notes.Api.Infrastructure;

namespace Notes.Api.Services;

/// <summary>Empties the trash of every user after <see cref="NotesOptions.TrashRetentionDays"/> (when configured).</summary>
public class TrashService(NotesDbContext db, NotebookService notebooks, IOptions<NotesOptions> options)
{
    /// <summary>
    /// Deletes notebooks (with everything below them, like a manual purge) and notes that have been
    /// in the trash longer than the retention period. Versions and tag links go with their notes.
    /// </summary>
    public async Task<CountsDto> EmptyExpiredAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var days = options.Value.TrashRetentionDays;
        if (days <= 0) return new CountsDto(0, 0);
        var cutoff = now - TimeSpan.FromDays(days);

        var expired = await db.Notebooks.AsNoTracking()
            .Where(n => n.DeletedTime != null && n.DeletedTime < cutoff)
            .Select(n => new { n.Id, n.UserId })
            .ToListAsync(ct);
        var deletedNotebooks = 0;
        var deletedNotes = 0;
        foreach (var user in expired.GroupBy(n => n.UserId))
        {
            var subtree = new HashSet<Guid>();
            foreach (var root in user) subtree.UnionWith(await notebooks.SubtreeIdsAsync(user.Key, root.Id, ct));
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            deletedNotes += await db.Notes.Where(n => n.UserId == user.Key && subtree.Contains(n.NotebookId)).ExecuteDeleteAsync(ct);
            deletedNotebooks += await db.Notebooks.Where(n => n.UserId == user.Key && subtree.Contains(n.Id)).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        }
        deletedNotes += await db.Notes.Where(n => n.DeletedTime != null && n.DeletedTime < cutoff).ExecuteDeleteAsync(ct);
        return new CountsDto(deletedNotebooks, deletedNotes);
    }
}
