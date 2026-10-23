using Microsoft.EntityFrameworkCore;
using Notes.Api.Data;
using Notes.Api.Infrastructure;

namespace Notes.Api.Services;

/// <summary>
/// Changes to a note sent with <c>PUT /api/notes/:id</c>; null fields stay as they are. <c>TodoDue</c> is
/// Unix ms (0 clears it); <c>BaseUpdatedTime</c> is the <c>updatedTime</c> the edit started from, a newer
/// note answers 409 unless <c>Force</c> is set.
/// </summary>
public record NoteUpdate(
    string? Title = null,
    string? Body = null,
    string? ParentId = null,
    bool? TodoCompleted = null,
    long? TodoDue = null,
    long? BaseUpdatedTime = null,
    bool Force = false);

/// <summary>Reading and writing notes, their trash state and their tags.</summary>
public class NoteService(NotesDbContext db, TimeProvider clock, NotebookService notebooks, VersionService versions)
{
    /// <summary>
    /// Notes in one list, newest first: active notes (optionally of one notebook and/or tag) or the trash.
    /// </summary>
    public async Task<List<NoteListItemDto>> ListAsync(Guid userId, string? notebookId, string? tagId, bool trash, CancellationToken ct = default)
    {
        var query = db.Notes.AsNoTracking().Where(n => n.UserId == userId);
        query = trash ? query.Where(n => n.DeletedTime != null) : query.Where(n => n.DeletedTime == null);
        if (!string.IsNullOrEmpty(notebookId))
        {
            var id = Ids.Parse(notebookId, "notebook id");
            query = query.Where(n => n.NotebookId == id);
        }
        if (!string.IsNullOrEmpty(tagId))
        {
            var id = Ids.Parse(tagId, "tag id");
            query = query.Where(n => n.NoteTags.Any(nt => nt.TagId == id));
        }
        var notes = await query
            .OrderByDescending(n => n.UpdatedTime)
            .Select(n => new
            {
                n.Id, n.Title, n.NotebookId, n.CreatedTime, n.UpdatedTime, n.DeletedTime, n.Preview, n.IsTodo,
                n.TodoCompletedTime, n.TodoDueTime,
            })
            .ToListAsync(ct);
        return notes.Select(n => new NoteListItemDto(
            Ids.Format(n.Id), n.Title, Ids.Format(n.NotebookId), Ids.Format(n.NotebookId), Times.ToMs(n.CreatedTime),
            Times.ToMs(n.UpdatedTime), Times.ToMs(n.DeletedTime), n.Preview, n.IsTodo, n.TodoCompletedTime != null,
            Times.ToMs(n.TodoDueTime))).ToList();
    }

    /// <summary>A note with body, notebook and tags, or 404.</summary>
    public async Task<NoteDto> GetAsync(Guid userId, string id, CancellationToken ct = default)
    {
        var noteId = Ids.Parse(id, "note id");
        return await DetailAsync(userId, noteId, ct) ?? throw ApiException.NotFound($"Note not found: {id}");
    }

    /// <summary>Creates a note or to-do in an active notebook.</summary>
    public async Task<NoteDto> CreateAsync(Guid userId, string? title, string? body, string parentId, bool isTodo, CancellationToken ct = default)
    {
        var notebook = await notebooks.RequireActiveAsync(userId, Ids.Parse(parentId, "notebook id"), ct);
        var now = Times.Now(clock);
        var note = new Note
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            NotebookId = notebook.Id,
            Title = Titles.Clean(title),
            Body = body ?? "",
            IsTodo = isTodo,
            CreatedTime = now,
            UpdatedTime = now,
        };
        note.Preview = TextPreview.For(note.Body, note.MarkupLanguage);
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);
        return (await DetailAsync(userId, note.Id, ct))!;
    }

    /// <summary>
    /// Applies an update. With <see cref="NoteUpdate.BaseUpdatedTime"/> set (and no <see cref="NoteUpdate.Force"/>),
    /// a note changed since then is refused with 409 and the current version in <c>details.current</c>.
    /// Title/body changes may first save the previous content as a version (see <see cref="VersionService"/>).
    /// </summary>
    public async Task<NoteDto> UpdateAsync(Guid userId, string id, NoteUpdate changes, CancellationToken ct = default)
    {
        var noteId = Ids.Parse(id, "note id");
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, ct)
            ?? throw ApiException.NotFound($"Note not found: {id}");

        if (!changes.Force && changes.BaseUpdatedTime is { } baseTime && Times.ToMs(note.UpdatedTime) != baseTime)
        {
            throw ApiException.Conflict("The note was changed elsewhere", new { current = await DetailAsync(userId, noteId, ct) });
        }

        var now = Times.Now(clock);
        var title = changes.Title is null ? note.Title : Titles.Clean(changes.Title);
        var body = changes.Body ?? note.Body;
        if (title != note.Title || body != note.Body)
        {
            await versions.SnapshotBeforeChangeAsync(note, now, ct);
            note.Title = title;
            note.Body = body;
            note.Preview = TextPreview.For(body, note.MarkupLanguage);
        }

        if (changes.ParentId is { } parentId)
        {
            var target = Ids.Parse(parentId, "notebook id");
            if (target != note.NotebookId)
            {
                await notebooks.RequireActiveAsync(userId, target, ct);
                note.NotebookId = target;
            }
        }
        if (changes.TodoCompleted is { } completed)
        {
            note.TodoCompletedTime = completed ? note.TodoCompletedTime ?? now : null;
        }
        if (changes.TodoDue is { } due)
        {
            if (due > 0 && !note.IsTodo) throw ApiException.BadRequest("Only to-dos can have a due date");
            note.TodoDueTime = due > 0 ? Times.FromMs(due) : null;
        }

        note.UpdatedTime = now;
        await db.SaveChangesAsync(ct);
        return (await DetailAsync(userId, noteId, ct))!;
    }

    /// <summary>
    /// Brings back a version's title and body. The current content is saved as a version first, so a
    /// restore can itself be undone. Conflict detection works as in <see cref="UpdateAsync"/>.
    /// </summary>
    public async Task<NoteDto> RestoreVersionAsync(Guid userId, string id, string versionId, long? baseUpdatedTime, CancellationToken ct = default)
    {
        var note = await FindAsync(userId, id, ct);
        var version = await versions.FindAsync(userId, note.Id, versionId, ct);
        if (baseUpdatedTime is { } baseTime && Times.ToMs(note.UpdatedTime) != baseTime)
        {
            throw ApiException.Conflict("The note was changed elsewhere", new { current = await DetailAsync(userId, note.Id, ct) });
        }
        var now = Times.Now(clock);
        await versions.SnapshotBeforeChangeAsync(note, now, ct, force: true);
        note.Title = version.Title;
        note.Body = version.Body;
        note.Preview = TextPreview.For(note.Body, note.MarkupLanguage);
        note.UpdatedTime = now;
        await db.SaveChangesAsync(ct);
        return (await DetailAsync(userId, note.Id, ct))!;
    }

    /// <summary>Moves a note to the trash (no-op when it is already there).</summary>
    public async Task TrashAsync(Guid userId, string id, CancellationToken ct = default)
    {
        var note = await FindAsync(userId, id, ct);
        if (note.DeletedTime is not null) return;
        note.DeletedTime = Times.Now(clock);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Restores a note from the trash, together with its trashed parent notebooks.</summary>
    public async Task<NoteDto> RestoreAsync(Guid userId, string id, CancellationToken ct = default)
    {
        var note = await FindAsync(userId, id, ct);
        var all = await db.Notebooks.Where(n => n.UserId == userId).ToListAsync(ct);
        NotebookService.RestoreAncestors(all, note.NotebookId, Times.Now(clock));
        note.DeletedTime = null;
        await db.SaveChangesAsync(ct);
        return (await DetailAsync(userId, note.Id, ct))!;
    }

    /// <summary>Permanently deletes a note in the trash, with its versions and tag links.</summary>
    public async Task PurgeAsync(Guid userId, string id, CancellationToken ct = default)
    {
        var note = await FindAsync(userId, id, ct);
        if (note.DeletedTime is null) throw ApiException.Conflict("Only notes in the trash can be deleted permanently");
        db.Notes.Remove(note);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Tags an active note; an existing tag with the same title (case-insensitive) is reused.</summary>
    public async Task<NoteDto> AddTagAsync(Guid userId, string id, string title, CancellationToken ct = default)
    {
        var tagTitle = Titles.Require(title, "Tag title");
        var note = await FindAsync(userId, id, ct);
        if (note.DeletedTime is not null) throw ApiException.NotFound($"Note not found: {id}");

        var lower = tagTitle.ToLowerInvariant();
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.UserId == userId && t.Title.ToLower() == lower, ct);
        var now = Times.Now(clock);
        if (tag is null)
        {
            tag = new Tag { Id = Guid.NewGuid(), UserId = userId, Title = tagTitle, CreatedTime = now, UpdatedTime = now };
            db.Tags.Add(tag);
        }
        var tagId = tag.Id;
        if (!await db.NoteTags.AnyAsync(nt => nt.NoteId == note.Id && nt.TagId == tagId, ct))
        {
            db.NoteTags.Add(new NoteTag { NoteId = note.Id, TagId = tag.Id, CreatedTime = now });
        }
        await db.SaveChangesAsync(ct);
        return (await DetailAsync(userId, note.Id, ct))!;
    }

    /// <summary>Removes a tag from a note; the tag itself stays.</summary>
    public async Task<NoteDto> RemoveTagAsync(Guid userId, string id, string tagId, CancellationToken ct = default)
    {
        var note = await FindAsync(userId, id, ct);
        var tag = Ids.Parse(tagId, "tag id");
        await db.NoteTags.Where(nt => nt.NoteId == note.Id && nt.TagId == tag).ExecuteDeleteAsync(ct);
        return (await DetailAsync(userId, note.Id, ct))!;
    }

    /// <summary>Loads a note of the user (tracked), or throws 404.</summary>
    internal async Task<Note> FindAsync(Guid userId, string id, CancellationToken ct)
    {
        var noteId = Ids.Parse(id, "note id");
        return await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, ct)
            ?? throw ApiException.NotFound($"Note not found: {id}");
    }

    /// <summary>The API shape of a note, or null when the user has no such note.</summary>
    internal async Task<NoteDto?> DetailAsync(Guid userId, Guid noteId, CancellationToken ct)
    {
        var row = await db.Notes.AsNoTracking()
            .Where(n => n.Id == noteId && n.UserId == userId)
            .Select(n => new
            {
                Note = n,
                NotebookTitle = db.Notebooks.Where(nb => nb.Id == n.NotebookId).Select(nb => nb.Title).FirstOrDefault(),
                Tags = n.NoteTags.Select(nt => new { nt.Tag!.Id, nt.Tag.Title }).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        var n = row.Note;
        var tags = row.Tags
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TagRefDto(Ids.Format(t.Id), t.Title))
            .ToList();
        return new NoteDto(
            Ids.Format(n.Id), n.Title, n.Body, n.Preview, Ids.Format(n.NotebookId), Ids.Format(n.NotebookId),
            row.NotebookTitle ?? "", tags, Times.ToMs(n.CreatedTime), Times.ToMs(n.UpdatedTime), Times.ToMs(n.DeletedTime),
            n.IsTodo, n.TodoCompletedTime != null, Times.ToMs(n.TodoDueTime), n.MarkupLanguage);
    }
}
