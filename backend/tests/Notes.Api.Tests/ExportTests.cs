using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using Notes.Api.Services;

namespace Notes.Api.Tests;

public class ExportTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    [Fact]
    public async Task Exports_active_notes_as_markdown_with_front_matter_attachments_and_relative_links()
    {
        var client = await app.NewUserClientAsync();
        var personal = await client.CreateNotebookAsync("Personal");
        var recipes = await client.CreateNotebookAsync("Recipes: Asian", personal);

        var content = new ByteArrayContent([0x89, 0x50, 0x4e, 0x47, 1, 2, 3]);
        var upload = new HttpRequestMessage(HttpMethod.Post, "/api/resources") { Content = content };
        upload.Headers.Add("X-File-Name", "bowl.png");
        upload.Headers.Add("X-File-Type", "image/png");
        var image = (await (await client.SendAsync(upload)).ReadJsonAsync()).Str("id");

        var todo = (await client.CreateNoteAsync(personal, "Buy miso", "From the market", isTodo: true)).Str("id");
        var ramen = (await client.CreateNoteAsync(recipes, "Ramen", $"![Bowl](:/{image})\n\nSee [the list](:/{todo}) and [gone](:/{new string('a', 32)})")).Str("id");
        await client.PostJsonAsync($"/api/notes/{ramen}/tags", new { title = "cooking" });
        await client.CreateNoteAsync(recipes, "Ramen", "A second note with the same title");
        var trashed = (await client.CreateNoteAsync(personal, "Old")).Str("id");
        await client.DeleteJsonAsync($"/api/notes/{trashed}", HttpStatusCode.NoContent);
        // Another user's notes never end up in the export
        var other = await app.NewUserClientAsync();
        await other.CreateNoteAsync(await other.CreateNotebookAsync("Secret"), "Not yours");

        var response = await client.GetAsync("/api/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("attachment;", response.Content.Headers.ContentDisposition?.ToString());
        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync());
        string Read(string path)
        {
            using var reader = new StreamReader(zip.GetEntry(path)!.Open());
            return reader.ReadToEnd();
        }

        Assert.Equal(
            ["Personal/Buy miso.md", "Personal/Recipes_ Asian/Ramen (2).md", "Personal/Recipes_ Asian/Ramen.md", "_resources/bowl.png"],
            zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        // Both notes are called "Ramen"; which one gets the plain name depends on creation order
        var markdown = new[] { "Personal/Recipes_ Asian/Ramen.md", "Personal/Recipes_ Asian/Ramen (2).md" }
            .Select(Read).Single(m => m.Contains("![Bowl]"));
        Assert.StartsWith("---\ntitle: \"Ramen\"\n", markdown);
        Assert.Contains("tags:\n  - \"cooking\"\n", markdown);
        Assert.Contains("![Bowl](../../_resources/bowl.png)", markdown);
        Assert.Contains("[the list](../../Personal/Buy%20miso.md)", markdown);
        // Links to items that aren't exported stay as they are
        Assert.Contains($"[gone](:/{new string('a', 32)})", markdown);
        Assert.Contains("completed?: no", Read("Personal/Buy miso.md"));
        Assert.Equal(7, zip.GetEntry("_resources/bowl.png")!.Length);
    }

    [Fact]
    public async Task Export_needs_a_session()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/export")).StatusCode);
    }

    [Theory]
    [InlineData("a/b\\c:d", "a_b_c_d")]
    [InlineData("CON", "_CON")]
    [InlineData("dots...", "dots")]
    [InlineData("   ", "fallback")]
    public void File_names_are_safe_on_every_system(string title, string expected) =>
        Assert.Equal(expected, ExportService.SafeName(title, "fallback"));
}
