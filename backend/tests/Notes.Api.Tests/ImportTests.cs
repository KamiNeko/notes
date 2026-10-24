using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Notes.Api.Tests;

public class ImportTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    private static async Task<HttpResponseMessage> ImportAsync(HttpClient client, byte[] zip)
    {
        var content = new ByteArrayContent(zip);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return await client.PostAsync("/api/import", content);
    }

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(content);
            }
        }
        return buffer.ToArray();
    }

    private static (string, byte[]) Text(string name, string content) => (name, Encoding.UTF8.GetBytes(content));

    private static async Task<JsonElement> NoteAsync(HttpClient client, string title)
    {
        var id = (await client.GetJsonAsync("/api/notes")).EnumerateArray().Single(n => n.Str("title") == title).Str("id");
        return await client.GetJsonAsync($"/api/notes/{id}");
    }

    [Fact]
    public async Task The_own_export_imports_into_a_new_notebook_with_links_tags_and_to_dos()
    {
        var alice = await app.NewUserClientAsync();
        var personal = await alice.CreateNotebookAsync("Personal");
        var recipes = await alice.CreateNotebookAsync("Recipes", personal);
        var upload = new HttpRequestMessage(HttpMethod.Post, "/api/resources") { Content = new ByteArrayContent([0x89, 0x50, 0x4e, 0x47, 1, 2, 3]) };
        upload.Headers.Add("X-File-Name", "bowl.png");
        var image = (await (await alice.SendAsync(upload)).ReadJsonAsync()).Str("id");
        var todo = (await alice.CreateNoteAsync(personal, "Buy miso", "From the market", isTodo: true)).Str("id");
        await alice.PutJsonAsync($"/api/notes/{todo}", new { todoCompleted = true, todoDue = 1_800_000_000_000L });
        var ramen = (await alice.CreateNoteAsync(recipes, "Ramen: quick", $"![Bowl](:/{image})\n\nSee [the list](:/{todo}#shop) and [web](https://example.com)")).Str("id");
        await alice.PostJsonAsync($"/api/notes/{ramen}/tags", new { title = "cooking" });
        var original = await alice.GetJsonAsync($"/api/notes/{ramen}");
        var export = await (await alice.GetAsync("/api/export")).Content.ReadAsByteArrayAsync();

        var bob = await app.NewUserClientAsync();
        await bob.PostJsonAsync($"/api/notes/{ramen}/tags", new { title = "x" }, HttpStatusCode.NotFound);
        var own = (await bob.CreateNoteAsync(await bob.CreateNotebookAsync("Mine"), "Mine")).Str("id");
        await bob.PostJsonAsync($"/api/notes/{own}/tags", new { title = "Cooking" });
        var existingTag = (await bob.GetJsonAsync("/api/tags")).EnumerateArray().Single().Str("id");
        var response = await ImportAsync(bob, export);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.ReadJsonAsync();
        Assert.Equal(2, result.GetProperty("notes").GetInt32());
        Assert.Equal(2, result.GetProperty("notebooks").GetInt32());
        Assert.Equal(1, result.GetProperty("attachments").GetInt32());
        Assert.Equal(0, result.GetProperty("skippedCount").GetInt32());
        Assert.Equal("Import 2026-10-01 12:00", result.Str("notebookTitle"));

        // Import 2026-10-01 12:00 / Personal / Recipes
        var root = (await bob.GetJsonAsync("/api/notebooks")).EnumerateArray().Single(n => n.Str("id") == result.Str("notebookId"));
        var imported = root.GetProperty("children").EnumerateArray().Single();
        Assert.Equal("Personal", imported.Str("title"));
        Assert.Equal("Recipes", imported.GetProperty("children").EnumerateArray().Single().Str("title"));

        var copy = await NoteAsync(bob, "Ramen: quick");
        var list = await NoteAsync(bob, "Buy miso");
        Assert.NotEqual(ramen, copy.Str("id"));
        Assert.Equal("Recipes", copy.Str("notebookTitle"));
        Assert.Equal(original.Long("createdTime"), copy.Long("createdTime"));
        Assert.Equal(original.Long("updatedTime"), copy.Long("updatedTime"));
        // The tag joins Bob's existing one (tags are case-insensitive)
        Assert.Equal(existingTag, copy.GetProperty("tags").EnumerateArray().Single().Str("id"));
        var newImage = Assert.Single(System.Text.RegularExpressions.Regex.Matches(copy.Str("body"), @"!\[Bowl\]\(:/([0-9a-f]{32})\)")).Groups[1].Value;
        Assert.NotEqual(image, newImage);
        Assert.Equal([0x89, 0x50, 0x4e, 0x47, 1, 2, 3], await (await bob.GetAsync($"/api/resources/{newImage}")).Content.ReadAsByteArrayAsync());
        Assert.Contains($"[the list](:/{list.Str("id")}#shop)", copy.Str("body"));
        Assert.Contains("[web](https://example.com)", copy.Str("body"));
        Assert.Equal("From the market", list.Str("body"));
        Assert.True(list.GetProperty("isTodo").GetBoolean());
        Assert.True(list.GetProperty("todoCompleted").GetBoolean());
        Assert.Equal(1_800_000_000_000L, list.Long("todoDue"));
    }

    [Fact]
    public async Task Other_tools_archives_import_and_unsafe_entries_are_skipped()
    {
        var client = await app.NewUserClientAsync();
        var zip = Zip(
            Text("Vault\\Trips\\Lisbon.md", "# Lisbon\n\n![map](map%20old.png) and [notes](../Ideas.md) and [nowhere](missing.png)"),
            ("Vault/Trips/map old.png", [1, 2, 3]),
            Text("Vault/Ideas.md", "---\ntags: [travel, 'later']\n---\nPlain"),
            Text("../escape.md", "outside"),
            Text("/etc/passwd.md", "absolute"),
            Text("__MACOSX/Vault/._Ideas.md", "junk"),
            Text("Vault/.obsidian/app.json", "{}"),
            ("Vault/empty.txt", []),
            Text("Page.html", "<p>Hi <img src=\"Vault/Trips/map old.png\"></p>"));

        var result = await (await ImportAsync(client, zip)).ReadJsonAsync();

        Assert.Equal(3, result.GetProperty("notes").GetInt32());
        Assert.Equal(1, result.GetProperty("attachments").GetInt32());
        Assert.Equal(
            ["../escape.md: unsafe path", "/etc/passwd.md: unsafe path", "Vault/empty.txt: No file content received"],
            result.GetProperty("skipped").EnumerateArray().Select(s => $"{s.Str("path")}: {s.Str("reason")}").Order(StringComparer.Ordinal));
        var lisbon = await NoteAsync(client, "Lisbon");
        Assert.Equal("Trips", lisbon.Str("notebookTitle"));
        var ideas = await NoteAsync(client, "Ideas");
        Assert.Matches(@"^# Lisbon\n\n!\[map\]\(:/[0-9a-f]{32}\) and \[notes\]\(:/" + ideas.Str("id") + @"\) and \[nowhere\]\(missing.png\)$", lisbon.Str("body"));
        Assert.Equal("Plain", ideas.Str("body"));
        Assert.Equal(["later", "travel"], ideas.GetProperty("tags").EnumerateArray().Select(t => t.Str("title")).Order());
        var page = await NoteAsync(client, "Page");
        Assert.Equal(2, page.GetProperty("markupLanguage").GetInt32());
        Assert.Matches("<img src=\":/[0-9a-f]{32}\">", page.Str("body"));
    }

    [Fact]
    public async Task Broken_and_oversized_archives_are_refused_without_leftovers()
    {
        var client = await app.NewUserClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(client, Encoding.UTF8.GetBytes("not a zip"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(client, [])).StatusCode);

        using var limited = app.WithWebHostBuilder(builder => builder.UseSetting("Notes:MaxImportMb", "1").UseSetting("Notes:MaxUploadMb", "1"));
        using var small = limited.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        small.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var email = $"import-{Guid.NewGuid():N}@notes.test";
        using (var admin = await app.AdminClientAsync()) await admin.PostJsonAsync("/api/admin/users", new { email, password = "user-password" }, HttpStatusCode.Created);
        (await small.PostAsync("/api/auth/login", JsonContent(new { email, password = "user-password" }))).EnsureSuccessStatusCode();

        // A ZIP bomb: a few KB that unpack to 11 MB (more than 10 × 1 MB)
        var bomb = await ImportAsync(small, Zip(("zeros.bin", new byte[11 * 1024 * 1024])));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, bomb.StatusCode);
        Assert.Contains("unpacks to more than 10 MB", (await bomb.ReadJsonAsync()).Str("error"));

        // A file past Notes__MaxUploadMb is skipped, the rest imports
        var mixed = await (await ImportAsync(small, Zip(("big.bin", new byte[2 * 1024 * 1024]), Text("Note.md", "ok")))).ReadJsonAsync();
        Assert.Equal(1, mixed.GetProperty("notes").GetInt32());
        Assert.Equal("larger than 1 MB", mixed.GetProperty("skipped")[0].Str("reason"));

        // The upload itself is limited too
        var random = new byte[2 * 1024 * 1024];
        Random.Shared.NextBytes(random);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await ImportAsync(small, Zip(("noise.bin", random)))).StatusCode);

        Assert.False(Directory.Exists(Path.Combine(app.DataDirectory, "tmp")) && Directory.EnumerateFiles(Path.Combine(app.DataDirectory, "tmp")).Any());
    }

    private static HttpContent JsonContent(object value) => System.Net.Http.Json.JsonContent.Create(value);
}
