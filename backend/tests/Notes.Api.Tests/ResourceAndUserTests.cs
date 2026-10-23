using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Notes.Api.Tests;

public class ResourceAndUserTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] data, string name, string type)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/resources") { Content = content };
        request.Headers.Add("X-File-Name", Uri.EscapeDataString(name));
        request.Headers.Add("X-File-Type", type);
        return await client.SendAsync(request);
    }

    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 0];

    [Fact]
    public async Task Upload_and_serve_attachments_safely()
    {
        var client = await app.NewUserClientAsync();
        var upload = await UploadAsync(client, Png, "Bild ä.png", "");
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var resource = await upload.ReadJsonAsync();
        Assert.Equal("image/png", resource.Str("mime"));
        Assert.Equal(Png.Length, resource.Long("size"));

        var image = await client.GetAsync($"/api/resources/{resource.Str("id")}");
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await image.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("inline;", image.Content.Headers.GetValues("Content-Disposition").Single());
        Assert.Contains("filename*=UTF-8''Bild%20%C3%A4.png", image.Content.Headers.GetValues("Content-Disposition").Single());
        Assert.Contains("sandbox", image.Headers.GetValues("Content-Security-Policy").Single());

        // HTML can never be served as a document from our origin
        var html = await (await UploadAsync(client, Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "x.html", "text/html")).ReadJsonAsync();
        var served = await client.GetAsync($"/api/resources/{html.Str("id")}");
        Assert.Equal("application/octet-stream", served.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("attachment;", served.Content.Headers.GetValues("Content-Disposition").Single());

        var svg = await (await UploadAsync(client, Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'/>"), "a.svg", "")).ReadJsonAsync();
        var svgServed = await client.GetAsync($"/api/resources/{svg.Str("id")}");
        Assert.Equal("image/svg+xml", svgServed.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("attachment;", svgServed.Content.Headers.GetValues("Content-Disposition").Single());

        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(client, [], "empty.txt", "text/plain")).StatusCode);

        await client.DeleteJsonAsync($"/api/resources/{resource.Str("id")}", HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/resources/{resource.Str("id")}")).StatusCode);
    }

    [Fact]
    public async Task Users_only_ever_see_their_own_data()
    {
        var alice = await app.NewUserClientAsync();
        var bob = await app.NewUserClientAsync();
        var notebook = await alice.CreateNotebookAsync("Alice's");
        var note = (await alice.CreateNoteAsync(notebook, "Private", "secret words")).Str("id");
        await alice.PostJsonAsync($"/api/notes/{note}/tags", new { title = "mine" });
        var resource = (await (await UploadAsync(alice, Png, "a.png", "image/png")).ReadJsonAsync()).Str("id");

        Assert.Equal(0, (await bob.GetJsonAsync("/api/notebooks")).GetArrayLength());
        Assert.Equal(0, (await bob.GetJsonAsync("/api/notes")).GetArrayLength());
        Assert.Equal(0, (await bob.GetJsonAsync("/api/tags")).GetArrayLength());
        Assert.Equal(0, (await bob.GetJsonAsync("/api/search?q=secret")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/notes/{note}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/resources/{resource}")).StatusCode);
        await bob.PutJsonAsync($"/api/notes/{note}", new { body = "hacked" }, HttpStatusCode.NotFound);
        await bob.PostJsonAsync("/api/notes", new { parentId = notebook, title = "x" }, HttpStatusCode.NotFound);
        await bob.DeleteJsonAsync($"/api/notebooks/{notebook}", HttpStatusCode.NotFound);
        await bob.DeleteJsonAsync($"/api/resources/{resource}", HttpStatusCode.NotFound);
        Assert.Equal("secret words", (await alice.GetJsonAsync($"/api/notes/{note}")).Str("body"));
    }

    [Fact]
    public async Task Only_admins_manage_users()
    {
        var user = await app.NewUserClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/users")).StatusCode);
        await user.PostJsonAsync("/api/admin/users", new { email = "x@notes.test", password = "password1" }, HttpStatusCode.Forbidden);

        var admin = await app.AdminClientAsync();
        var email = $"new-{Guid.NewGuid():N}@notes.test";
        var created = await admin.PostJsonAsync("/api/admin/users", new { email = email.ToUpperInvariant(), password = "password1", displayName = "New" }, HttpStatusCode.Created);
        Assert.Equal(email, created.Str("email"));
        Assert.False(created.GetProperty("isAdmin").GetBoolean());
        await admin.PostJsonAsync("/api/admin/users", new { email, password = "password1" }, HttpStatusCode.Conflict);
        await admin.PostJsonAsync("/api/admin/users", new { email = "bad", password = "password1" }, HttpStatusCode.BadRequest);
        await admin.PostJsonAsync("/api/admin/users", new { email = "short@notes.test", password = "short" }, HttpStatusCode.BadRequest);
        Assert.Contains(email, (await admin.GetJsonAsync("/api/admin/users")).EnumerateArray().Select(u => u.Str("email")));

        // A password reset ends the user's sessions
        var session = await app.SignInAsync(email, "password1");
        await admin.PutJsonAsync($"/api/admin/users/{created.Str("id")}", new { password = "password2" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/check")).StatusCode);
        await app.SignInAsync(email, "password2");
    }

    [Fact]
    public async Task Deleting_a_user_deletes_their_data_but_never_the_last_admin()
    {
        var admin = await app.AdminClientAsync();
        var email = $"gone-{Guid.NewGuid():N}@notes.test";
        var doomed = await app.NewUserClientAsync(email);
        var notebook = await doomed.CreateNotebookAsync("Doomed");
        var child = await doomed.CreateNotebookAsync("Child", notebook);
        var note = (await doomed.CreateNoteAsync(child, "Note", "v1")).Str("id");
        await doomed.PutJsonAsync($"/api/notes/{note}", new { body = "v2" });
        await doomed.PostJsonAsync($"/api/notes/{note}/tags", new { title = "t" });
        await UploadAsync(doomed, Png, "a.png", "image/png");

        var users = await admin.GetJsonAsync("/api/admin/users");
        var id = users.EnumerateArray().Single(u => u.Str("email") == email).Str("id");
        var self = users.EnumerateArray().Single(u => u.Str("email") == "admin@notes.test").Str("id");

        await admin.DeleteJsonAsync($"/api/admin/users/{id}", HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.Unauthorized, (await doomed.GetAsync("/api/notes")).StatusCode);
        Assert.False(Directory.Exists(Path.Combine(app.DataDirectory, "attachments", id)));
        var left = await app.WithServiceAsync<Data.NotesDbContext, int>(db => Task.FromResult(db.Notes.Count(n => n.UserId == Guid.ParseExact(id, "N"))));
        Assert.Equal(0, left);

        await admin.DeleteJsonAsync($"/api/admin/users/{self}", HttpStatusCode.Conflict);
        await admin.PutJsonAsync($"/api/admin/users/{self}", new { isAdmin = false }, HttpStatusCode.Conflict);
    }
}
