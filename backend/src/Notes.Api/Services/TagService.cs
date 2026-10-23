using Microsoft.EntityFrameworkCore;
using Notes.Api.Data;
using Notes.Api.Infrastructure;

namespace Notes.Api.Services;

/// <summary>Tags: list with counts, rename, delete. Tags are created by tagging a note (<see cref="NoteService.AddTagAsync"/>).</summary>
public class TagService(NotesDbContext db, TimeProvider clock)
{
    /// <summary>All tags of the user, sorted by title, with the number of active notes carrying them.</summary>
    public async Task<List<TagDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var tags = await db.Tags.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new { t.Id, t.Title, Count = t.NoteTags.Count(nt => nt.Note!.DeletedTime == null) })
            .ToListAsync(ct);
        return tags
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TagDto(Ids.Format(t.Id), t.Title, t.Count))
            .ToList();
    }

    /// <summary>Renames a tag; 409 when another tag already has the title.</summary>
    public async Task<TagRefDto> RenameAsync(Guid userId, string id, string title, CancellationToken ct = default)
    {
        var tagId = Ids.Parse(id, "tag id");
        var tagTitle = Titles.Require(title, "Tag title");
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == tagId && t.UserId == userId, ct)
            ?? throw ApiException.NotFound($"Tag not found: {id}");
        var lower = tagTitle.ToLowerInvariant();
        var clash = await db.Tags.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Id != tagId && t.Title.ToLower() == lower, ct);
        if (clash is not null) throw ApiException.Conflict($"A tag named \"{clash.Title}\" already exists");
        tag.Title = tagTitle;
        tag.UpdatedTime = Times.Now(clock);
        await db.SaveChangesAsync(ct);
        return new TagRefDto(Ids.Format(tag.Id), tag.Title);
    }

    /// <summary>Deletes a tag and removes it from all notes. Returns how many notes had it.</summary>
    public async Task<int> DeleteAsync(Guid userId, string id, CancellationToken ct = default)
    {
        var tagId = Ids.Parse(id, "tag id");
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == tagId && t.UserId == userId, ct)
            ?? throw ApiException.NotFound($"Tag not found: {id}");
        var notes = await db.NoteTags.CountAsync(nt => nt.TagId == tagId, ct);
        db.Tags.Remove(tag);
        await db.SaveChangesAsync(ct);
        return notes;
    }
}
