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
    private string UserDirectory(Guid userId) =>
        Path.Combine(Path.GetFullPath(options.Value.DataDirectory), "attachments", Ids.Format(userId));

    private string FilePath(Guid userId, Guid resourceId) => Path.Combine(UserDirectory(userId), Ids.Format(resourceId));

    /// <summary>Stores an uploaded file. The MIME type is taken from the browser, the extension or the content.</summary>
    public async Task<ResourceDto> CreateAsync(Guid userId, string fileName, string? declaredType, Stream content, CancellationToken ct = default)
    {
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

        db.Resources.Add(resource);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
        return new ResourceDto(Ids.Format(resource.Id), resource.FileName, resource.MimeType, resource.Size);
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

    /// <summary>Deletes all attachment files of a user (their rows go with the user).</summary>
    public void DeleteAllFiles(Guid userId)
    {
        var directory = UserDirectory(userId);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
