using System.Net;

namespace Notes.Api.Tests;

public class TagAndSearchTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    [Fact]
    public async Task Tags_are_reused_case_insensitively_and_counted_for_active_notes()
    {
        var client = await app.NewUserClientAsync();
        var notebook = await client.CreateNotebookAsync("Inbox");
        var a = (await client.CreateNoteAsync(notebook, "A")).Str("id");
        app.Clock.Advance(TimeSpan.FromSeconds(1));
        var b = (await client.CreateNoteAsync(notebook, "B")).Str("id");

        var tagged = await client.PostJsonAsync($"/api/notes/{a}/tags", new { title = "Travel" });
        var tagId = Assert.Single(tagged.GetProperty("tags").EnumerateArray()).Str("id");
        await client.PostJsonAsync($"/api/notes/{b}/tags", new { title = "travel " });
        await client.PostJsonAsync($"/api/notes/{b}/tags", new { title = "TRAVEL" });

        var tag = Assert.Single((await client.GetJsonAsync("/api/tags")).EnumerateArray());
        Assert.Equal(tagId, tag.Str("id"));
        Assert.Equal(2, tag.GetProperty("noteCount").GetInt32());
        Assert.Equal(["B", "A"], (await client.GetJsonAsync($"/api/notes?tagId={tagId}")).Titles());

        await client.DeleteJsonAsync($"/api/notes/{b}", HttpStatusCode.NoContent);
        Assert.Equal(1, (await client.GetJsonAsync("/api/tags"))[0].GetProperty("noteCount").GetInt32());
        // A note in the trash can't be tagged
        await client.PostJsonAsync($"/api/notes/{b}/tags", new { title = "x" }, HttpStatusCode.NotFound);

        var untagged = await client.DeleteJsonAsync($"/api/notes/{a}/tags/{tagId}");
        Assert.Equal(0, untagged.GetProperty("tags").GetArrayLength());
    }

    [Fact]
    public async Task Rename_refuses_duplicates_and_delete_unlinks()
    {
        var client = await app.NewUserClientAsync();
        var notebook = await client.CreateNotebookAsync("Inbox");
        var note = (await client.CreateNoteAsync(notebook, "A")).Str("id");
        await client.PostJsonAsync($"/api/notes/{note}/tags", new { title = "one" });
        var tags = (await client.PostJsonAsync($"/api/notes/{note}/tags", new { title = "two" })).GetProperty("tags");
        Assert.Equal(["one", "two"], tags.Titles());
        var two = tags[1].Str("id");

        await client.PutJsonAsync($"/api/tags/{two}", new { title = "ONE" }, HttpStatusCode.Conflict);
        Assert.Equal("Two", (await client.PutJsonAsync($"/api/tags/{two}", new { title = "Two" })).Str("title"));

        var deleted = await client.DeleteJsonAsync($"/api/tags/{two}");
        Assert.Equal(1, deleted.GetProperty("notes").GetInt32());
        Assert.Equal(["one"], (await client.GetJsonAsync($"/api/notes/{note}")).GetProperty("tags").Titles());
    }

    [Fact]
    public async Task Search_supports_words_phrases_negation_and_filters()
    {
        var client = await app.NewUserClientAsync();
        var personal = await client.CreateNotebookAsync("Personal");
        var recipes = await client.CreateNotebookAsync("Recipes", personal);
        var work = await client.CreateNotebookAsync("Work");
        var ramen = (await client.CreateNoteAsync(recipes, "Miso Ramen", "Whisk the miso into the stock. 100% tasty_stuff")).Str("id");
        await client.PostJsonAsync($"/api/notes/{ramen}/tags", new { title = "cooking" });
        await client.CreateNoteAsync(work, "Stock report", "Quarterly numbers");
        var todo = (await client.CreateNoteAsync(personal, "Buy miso", "", isTodo: true)).Str("id");
        await client.PutJsonAsync($"/api/notes/{todo}", new { todoCompleted = true });
        var trashed = (await client.CreateNoteAsync(work, "Miso in the trash")).Str("id");
        await client.DeleteJsonAsync($"/api/notes/{trashed}", HttpStatusCode.NoContent);

        async Task<List<string>> Search(string q) =>
            (await client.GetJsonAsync($"/api/search?q={Uri.EscapeDataString(q)}")).Titles();

        // Title hits rank first, trashed notes never show up
        Assert.Equal(["Buy miso", "Miso Ramen"], (await Search("miso")).Order().ToList());
        Assert.Equal(["Miso Ramen", "Stock report"], (await Search("stock")).Order().ToList());
        Assert.Equal(["Miso Ramen"], await Search("\"miso into\""));
        Assert.Equal(["Miso Ramen"], await Search("stock -quarterly"));
        Assert.Equal(["Miso Ramen"], await Search("tag:cook*"));
        Assert.Equal(["Buy miso"], await Search("miso -tag:cooking"));
        Assert.Equal(["Buy miso", "Miso Ramen"], (await Search("notebook:personal")).Order().ToList());
        Assert.Equal(["Stock report"], await Search("-notebook:personal"));
        Assert.Equal(["Buy miso"], await Search("type:todo"));
        Assert.Equal(["Buy miso"], await Search("iscompleted:1"));
        Assert.Empty(await Search("iscompleted:0"));
        // LIKE wildcards are literal text
        Assert.Equal(["Miso Ramen"], await Search("100%"));
        Assert.Empty(await Search("tasty%stuff"));
        Assert.Equal(["Miso Ramen"], await Search("tasty_stuff"));
        Assert.Empty(await Search("   "));

        var result = (await client.GetJsonAsync("/api/search?q=whisk"))[0].GetProperty("matches");
        Assert.True(result.GetProperty("inBody").GetBoolean());
        Assert.False(result.GetProperty("inTitle").GetBoolean());
        Assert.Contains("Whisk the miso", result.Str("snippet"));
        Assert.Equal(["whisk"], result.GetProperty("terms").EnumerateArray().Select(t => t.GetString()!));
    }
}
