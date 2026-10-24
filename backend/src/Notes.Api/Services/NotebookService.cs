using Microsoft.EntityFrameworkCore;
using Notes.Api.Data;
using Notes.Api.Infrastructure;

namespace Notes.Api.Services;

/// <summary>
/// Notebooks: the tree, create/rename/move, and the trash. Trashing a notebook stamps it and every
/// active notebook and note below it with the same <c>DeletedTime</c>; restoring brings back exactly
/// the items with that stamp (plus trashed parent notebooks), so items trashed on their own earlier
/// stay in the trash.
/// </summary>
public class NotebookService(NotesDbContext db, TimeProvider clock)
{
    /// <summary>The user's active notebooks as a tree, sorted by title, with direct note counts.</summary>
    public async Task<List<NotebookDto>> GetTreeAsync(Guid userId, CancellationToken ct = default)
    {
        var notebooks = await db.Notebooks.AsNoTracking()
            .Where(n => n.UserId == userId && n.DeletedTime == null)
            .ToListAsync(ct);
        var counts = await db.Notes
            .Where(n => n.UserId == userId && n.DeletedTime == null)
            .GroupBy(n => n.NotebookId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        var nodes = notebooks.ToDictionary(
            n => n.Id,
            n => new NotebookDto(Ids.Format(n.Id), n.Title, Ids.Format(n.ParentId), [], counts.GetValueOrDefault(n.Id),
                Times.ToMs(n.CreatedTime), Times.ToMs(n.UpdatedTime)));
        var roots = new List<NotebookDto>();
        foreach (var notebook in notebooks)
        {
            var node = nodes[notebook.Id];
            if (notebook.ParentId is { } parentId && nodes.TryGetValue(parentId, out var parent)) parent.Children.Add(node);
            else roots.Add(node);
        }
        SortByTitle(roots);
        return roots;
    }

    private static void SortByTitle(List<NotebookDto> nodes)
    {
        nodes.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Title, b.Title));
        foreach (var node in nodes) SortByTitle(node.Children);
    }

    /// <summary>
    /// Trashed notebooks whose parent is not trashed (the top of each trashed subtree), newest first,
    /// with the counts of what a restore brings back and of notes trashed earlier on their own.
    /// </summary>
    public async Task<List<TrashedNotebookDto>> GetTrashAsync(Guid userId, CancellationToken ct = default)
    {
        var all = await db.Notebooks.AsNoTracking().Where(n => n.UserId == userId).ToListAsync(ct);
        var byId = all.ToDictionary(n => n.Id);
        var notes = await db.Notes.AsNoTracking()
            .Where(n => n.UserId == userId)
            .Select(n => new { n.NotebookId, n.DeletedTime })
            .ToListAsync(ct);
        var notesByNotebook = notes.ToLookup(n => n.NotebookId);
        var childrenOf = all.Where(n => n.ParentId != null).ToLookup(n => n.ParentId!.Value);

        bool IsTrashed(Guid? id) => id is { } value && byId.TryGetValue(value, out var nb) && nb.DeletedTime != null;

        var result = new List<TrashedNotebookDto>();
        foreach (var root in all.Where(n => n.DeletedTime != null && !IsTrashed(n.ParentId)))
        {
            int noteCount = 0, notebookCount = 0, earlierNoteCount = 0;
            var stack = new Stack<(Notebook Notebook, bool Restored)>();
            stack.Push((root, true));
            while (stack.Count > 0)
            {
                var (notebook, restored) = stack.Pop();
                if (notebook != root && restored) notebookCount++;
                foreach (var note in notesByNotebook[notebook.Id])
                {
                    if (restored && (note.DeletedTime == null || note.DeletedTime == root.DeletedTime)) noteCount++;
                    else earlierNoteCount++;
                }
                foreach (var child in childrenOf[notebook.Id])
                {
                    stack.Push((child, restored && (child.DeletedTime == null || child.DeletedTime == root.DeletedTime)));
                }
            }
            result.Add(new TrashedNotebookDto(Ids.Format(root.Id), root.Title, Ids.Format(root.ParentId),
                Times.ToMs(root.DeletedTime), noteCount, notebookCount, earlierNoteCount));
        }
        return result.OrderByDescending(n => n.DeletedTime).ToList();
    }

    /// <summary>Creates a notebook, at the top level or inside an active notebook.</summary>
    public async Task<NotebookSummaryDto> CreateAsync(Guid userId, string title, string? parentId, CancellationToken ct = default)
    {
        var cleanTitle = Titles.Require(title, "Notebook title");
        Guid? parent = null;
        if (!string.IsNullOrEmpty(parentId))
        {
            parent = Ids.Parse(parentId, "notebook id");
            await RequireActiveAsync(userId, parent.Value, ct);
        }
        var now = Times.Now(clock);
        var notebook = new Notebook
        {
            Id = Guid.NewGuid(), UserId = userId, ParentId = parent, Title = cleanTitle, CreatedTime = now, UpdatedTime = now,
        };
        db.Notebooks.Add(notebook);
        await db.SaveChangesAsync(ct);
        return Summary(notebook);
    }

    /// <summary>Renames a notebook and/or moves it (an empty <paramref name="parentId"/> moves it to the top level).</summary>
    public async Task<NotebookSummaryDto> UpdateAsync(Guid userId, string id, string? title, string? parentId, CancellationToken ct = default)
    {
        var notebook = await RequireActiveAsync(userId, Ids.Parse(id, "notebook id"), ct);
        if (title is not null) notebook.Title = Titles.Require(title, "Notebook title");

        if (parentId is not null)
        {
            Guid? newParent = parentId == "" ? null : Ids.Parse(parentId, "notebook id");
            if (newParent != notebook.ParentId)
            {
                if (newParent is { } target)
                {
                    await RequireActiveAsync(userId, target, ct);
                    var parents = await db.Notebooks.AsNoTracking()
                        .Where(n => n.UserId == userId)
                        .ToDictionaryAsync(n => n.Id, n => n.ParentId, ct);
                    Guid? cursor = target;
                    var seen = new HashSet<Guid>();
                    while (cursor is { } current && seen.Add(current))
                    {
                        if (current == notebook.Id) throw ApiException.BadRequest("Cannot move a notebook into itself");
                        cursor = parents.GetValueOrDefault(current);
                    }
                }
                notebook.ParentId = newParent;
            }
        }

        notebook.UpdatedTime = Times.Now(clock);
        await db.SaveChangesAsync(ct);
        return Summary(notebook);
    }

    /// <summary>Moves a notebook with its active sub-notebooks and notes to the trash.</summary>
    public async Task<CountsDto> TrashAsync(Guid userId, string id, CancellationToken ct = default)
    {
        var notebook = await RequireActiveAsync(userId, Ids.Parse(id, "notebook id"), ct);
        var subtree = await SubtreeIdsAsync(userId, notebook.Id, ct);
        var now = Times.Now(clock);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var notebooks = await db.Notebooks
            .Where(n => n.UserId == userId && subtree.Contains(n.Id) && n.DeletedTime == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.DeletedTime, now), ct);
        var notes = await db.Notes
            .Where(n => n.UserId == userId && subtree.Contains(n.NotebookId) && n.DeletedTime == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.DeletedTime, now), ct);
        await tx.CommitAsync(ct);
        return new CountsDto(notebooks, notes);
    }

    /// <summary>
    /// Restores a trashed notebook with the sub-notebooks and notes trashed together with it, and
    /// its trashed parent notebooks.
    /// </summary>
    public async Task<CountsDto> RestoreAsync(Guid userId, string id, CancellationToken ct = default)
    {
        var notebookId = Ids.Parse(id, "notebook id");
        var all = await db.Notebooks.Where(n => n.UserId == userId).ToListAsync(ct);
        var notebook = all.FirstOrDefault(n => n.Id == notebookId) ?? throw ApiException.NotFound($"Notebook not found: {id}");
        if (notebook.DeletedTime is not { } stamp) throw ApiException.Conflict("The notebook is not in the trash");

        var restored = new HashSet<Guid>();
        var childrenOf = all.Where(n => n.ParentId != null).ToLookup(n => n.ParentId!.Value);
        var stack = new Stack<Notebook>([notebook]);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.DeletedTime != stamp || !restored.Add(current.Id)) continue;
            foreach (var child in childrenOf[current.Id]) stack.Push(child);
        }
        var now = Times.Now(clock);
        foreach (var nb in all.Where(n => restored.Contains(n.Id)))
        {
            nb.DeletedTime = null;
            nb.UpdatedTime = now;
        }
        var ancestors = RestoreAncestors(all, notebook.ParentId, now);
        var notes = await db.Notes
            .Where(n => n.UserId == userId && restored.Contains(n.NotebookId) && n.DeletedTime == stamp)
            .ToListAsync(ct);
        foreach (var note in notes) note.DeletedTime = null;
        await db.SaveChangesAsync(ct);
        return new CountsDto(restored.Count + ancestors, notes.Count);
    }

    /// <summary>
    /// Restores the trashed notebooks on the way from <paramref name="notebookId"/> up to the top level
    /// (tracked entities of the user, saved by the caller), so restored items show up in the tree again.
    /// Returns how many were restored.
    /// </summary>
    internal static int RestoreAncestors(IEnumerable<Notebook> userNotebooks, Guid? notebookId, DateTimeOffset now)
    {
        var byId = userNotebooks.ToDictionary(n => n.Id);
        var seen = new HashSet<Guid>();
        var restored = 0;
        for (var cursor = notebookId; cursor is { } id && seen.Add(id) && byId.TryGetValue(id, out var notebook); cursor = notebook.ParentId)
        {
            if (notebook.DeletedTime is null) continue;
            notebook.DeletedTime = null;
            notebook.UpdatedTime = now;
            restored++;
        }
        return restored;
    }

    /// <summary>Permanently deletes a trashed notebook with everything below it, including notes trashed earlier.</summary>
    public async Task<CountsDto> PurgeAsync(Guid userId, string id, CancellationToken ct = default)
    {
        var notebookId = Ids.Parse(id, "notebook id");
        var notebook = await db.Notebooks.FirstOrDefaultAsync(n => n.Id == notebookId && n.UserId == userId, ct)
            ?? throw ApiException.NotFound($"Notebook not found: {id}");
        if (notebook.DeletedTime is null) throw ApiException.Conflict("Only notebooks in the trash can be deleted permanently");
        var subtree = await SubtreeIdsAsync(userId, notebook.Id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var notes = await db.Notes.Where(n => n.UserId == userId && subtree.Contains(n.NotebookId)).ExecuteDeleteAsync(ct);
        // One statement: the parent foreign key is checked at its end, so the order doesn't matter
        var notebooks = await db.Notebooks.Where(n => n.UserId == userId && subtree.Contains(n.Id)).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return new CountsDto(notebooks, notes);
    }

    /// <summary>Loads an active notebook of the user, or throws 404.</summary>
    internal async Task<Notebook> RequireActiveAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var notebook = await db.Notebooks.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
        if (notebook is null || notebook.DeletedTime is not null) throw ApiException.NotFound($"Notebook not found: {Ids.Format(id)}");
        return notebook;
    }

    /// <summary>The notebook and all notebooks below it (trashed or not).</summary>
    internal async Task<List<Guid>> SubtreeIdsAsync(Guid userId, Guid rootId, CancellationToken ct)
    {
        var parents = await db.Notebooks.AsNoTracking()
            .Where(n => n.UserId == userId && n.ParentId != null)
            .Select(n => new { n.Id, ParentId = n.ParentId!.Value })
            .ToListAsync(ct);
        var childrenOf = parents.ToLookup(p => p.ParentId, p => p.Id);
        var result = new List<Guid>();
        var seen = new HashSet<Guid>();
        var stack = new Stack<Guid>([rootId]);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current)) continue;
            result.Add(current);
            foreach (var child in childrenOf[current]) stack.Push(child);
        }
        return result;
    }

    private static NotebookSummaryDto Summary(Notebook n) => new(Ids.Format(n.Id), n.Title, Ids.Format(n.ParentId));
}
