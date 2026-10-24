using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Notes.Api.Data;
using Notes.Api.Infrastructure;
using Notes.Api.Services;

namespace Notes.Api.Tests;

public class AttachmentCleanupTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    private static async Task<string> UploadAsync(HttpClient client, string name)
    {
        var content = new ByteArrayContent([1, 2, 3, 4]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/resources") { Content = content };
        request.Headers.Add("X-File-Name", name);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).Str("id");
    }

    private Task<(int Attachments, int Files)> DeleteUnusedAsync(int days) =>
        app.WithServiceAsync<NotesDbContext, (int, int)>(db =>
        {
            var options = Options.Create(new NotesOptions { DataDirectory = app.DataDirectory, AttachmentCleanupDays = days });
            return new ResourceService(db, app.Clock, options).DeleteUnusedAsync(app.Clock.GetUtcNow());
        });

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string id) => (await client.GetAsync($"/api/resources/{id}")).StatusCode;

    [Fact]
    public async Task Attachments_nothing_links_to_are_deleted_after_the_grace_period()
    {
        var client = await app.NewUserClientAsync();
        var notebook = await client.CreateNotebookAsync("Inbox");
        var inNote = await UploadAsync(client, "in-note.png");
        var inTrash = await UploadAsync(client, "in-trash.png");
        var inVersion = await UploadAsync(client, "in-version.png");
        var unused = await UploadAsync(client, "unused.png");
        await client.CreateNoteAsync(notebook, "Linked", $"![](:/{inNote})");
        var trashed = (await client.CreateNoteAsync(notebook, "Trashed", $"[file](:/{inTrash})")).Str("id");
        await client.DeleteJsonAsync($"/api/notes/{trashed}", HttpStatusCode.NoContent);
        var edited = (await client.CreateNoteAsync(notebook, "Edited", $"<img src=\":/{inVersion}\">")).Str("id");
        await client.PutJsonAsync($"/api/notes/{edited}", new { body = "link removed, the version keeps it" });

        app.Clock.Advance(TimeSpan.FromDays(8));
        var fresh = await UploadAsync(client, "fresh.png");

        // Off by default
        Assert.Equal((0, 0), await DeleteUnusedAsync(0));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, unused));

        var (attachments, _) = await DeleteUnusedAsync(7);
        Assert.True(attachments >= 1);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(client, unused));
        Assert.False(File.Exists(Path.Combine(app.DataDirectory, "attachments", (await UserIdAsync(client)), unused)));
        // Linked from a note, a trashed note or a version, or younger than the grace period: kept
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, inNote));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, inTrash));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, inVersion));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, fresh));
    }

    [Fact]
    public async Task Files_without_a_database_row_are_deleted_after_the_grace_period()
    {
        var client = await app.NewUserClientAsync();
        var kept = await UploadAsync(client, "kept.png");
        var directory = Path.Combine(app.DataDirectory, "attachments", await UserIdAsync(client));
        var oldStray = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        var oldUpload = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".upload");
        var newStray = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        foreach (var path in new[] { oldStray, oldUpload, newStray }) await File.WriteAllBytesAsync(path, [9]);
        var old = app.Clock.GetUtcNow().UtcDateTime.AddDays(-10);
        File.SetLastWriteTimeUtc(oldStray, old);
        File.SetLastWriteTimeUtc(oldUpload, old);
        File.SetLastWriteTimeUtc(Path.Combine(directory, kept), old);
        File.SetLastWriteTimeUtc(newStray, app.Clock.GetUtcNow().UtcDateTime);

        var (_, files) = await DeleteUnusedAsync(7);

        Assert.True(files >= 2);
        Assert.False(File.Exists(oldStray));
        Assert.False(File.Exists(oldUpload));
        Assert.True(File.Exists(newStray));
        // The uploaded attachment has a row; it is only judged by its links once it is old enough
        Assert.True(File.Exists(Path.Combine(directory, kept)));
    }

    private static async Task<string> UserIdAsync(HttpClient client) =>
        (await client.GetJsonAsync("/api/auth/check")).GetProperty("user").Str("id");
}
