using System.Net;

namespace Notes.Api.Tests;

public class NoteTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    [Fact]
    public async Task Create_get_and_list_newest_first()
    {
        var client = await app.NewUserClientAsync();
        var notebook = await client.CreateNotebookAsync("Inbox");
        var first = await client.CreateNoteAsync(notebook, "First", "# Heading\n\nSome **bold** text and a [link](https://example.com).");
        app.Clock.Advance(TimeSpan.FromSeconds(1));
        await client.CreateNoteAsync(notebook, "Second");

        Assert.Equal("Heading Some bold text and a link.", first.Str("preview"));
        Assert.Equal(notebook, first.Str("notebookId"));
        Assert.Equal("Inbox", first.Str("notebookTitle"));
        Assert.Equal(1, first.GetProperty("markupLanguage").GetInt32());

        var list = await client.GetJsonAsync($"/api/notes?notebookId={notebook}");
        Assert.Equal(["Second", "First"], list.Titles());
        Assert.Equal(["Second", "First"], (await client.GetJsonAsync("/api/notes")).Titles());
        await client.PostJsonAsync("/api/notes", new { title = "x" }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_detects_conflicts_unless_forced()
    {
        var client = await app.NewUserClientAsync();
        var notebook = await client.CreateNotebookAsync("Inbox");
        var note = await client.CreateNoteAsync(notebook, "Draft", "v1");
        var id = note.Str("id");
        var baseTime = note.Long("updatedTime");

        app.Clock.Advance(TimeSpan.FromSeconds(2));
        var saved = await client.PutJsonAsync($"/api/notes/{id}", new { body = "v2", baseUpdatedTime = baseTime });
        Assert.Equal("v2", saved.Str("body"));
        Assert.True(saved.Long("updatedTime") > baseTime);

        // A second editor still on the old base gets a 409 with the current note
        var conflict = await client.PutJsonAsync($"/api/notes/{id}", new { body = "other", baseUpdatedTime = baseTime }, HttpStatusCode.Conflict);
        Assert.Equal("v2", conflict.GetProperty("details").GetProperty("current").Str("body"));

        var forced = await client.PutJsonAsync($"/api/notes/{id}", new { body = "other", baseUpdatedTime = baseTime, force = true });
        Assert.Equal("other", forced.Str("body"));
    }

    [Fact]
    public async Task Moving_needs_an_active_notebook()
    {
        var client = await app.NewUserClientAsync();
        var a = await client.CreateNotebookAsync("A");
        var b = await client.CreateNotebookAsync("B");
        var id = (await client.CreateNoteAsync(a, "Note")).Str("id");

        Assert.Equal(b, (await client.PutJsonAsync($"/api/notes/{id}", new { parentId = b })).Str("notebookId"));
        await client.DeleteJsonAsync($"/api/notebooks/{a}");
        await client.PutJsonAsync($"/api/notes/{id}", new { parentId = a }, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Todos_can_be_completed_and_get_due_dates()
    {
        var client = await app.NewUserClientAsync();
        var notebook = await client.CreateNotebookAsync("Inbox");
        var todo = (await client.CreateNoteAsync(notebook, "Pay bill", isTodo: true)).Str("id");
        var note = (await client.CreateNoteAsync(notebook, "Plain")).Str("id");
        var due = new DateTimeOffset(2026, 12, 24, 18, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        var updated = await client.PutJsonAsync($"/api/notes/{todo}", new { todoCompleted = true, todoDue = due });
        Assert.True(updated.GetProperty("todoCompleted").GetBoolean());
        Assert.Equal(due, updated.Long("todoDue"));
        Assert.Equal(0, (await client.PutJsonAsync($"/api/notes/{todo}", new { todoDue = 0 })).Long("todoDue"));
        Assert.False((await client.PutJsonAsync($"/api/notes/{todo}", new { todoCompleted = false })).GetProperty("todoCompleted").GetBoolean());

        await client.PutJsonAsync($"/api/notes/{note}", new { todoDue = due }, HttpStatusCode.BadRequest);
        await client.PutJsonAsync($"/api/notes/{todo}", new { todoDue = -5 }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Trash_restore_and_permanent_delete()
    {
        var client = await app.NewUserClientAsync();
        var notebook = await client.CreateNotebookAsync("Inbox");
        var id = (await client.CreateNoteAsync(notebook, "Note")).Str("id");

        await client.DeleteJsonAsync($"/api/notes/{id}?permanent=true", HttpStatusCode.Conflict);
        await client.DeleteJsonAsync($"/api/notes/{id}", HttpStatusCode.NoContent);
        Assert.Empty((await client.GetJsonAsync("/api/notes")).Titles());
        Assert.True((await client.GetJsonAsync($"/api/notes/{id}")).Long("deletedTime") > 0);

        await client.PostJsonAsync($"/api/notes/{id}/restore");
        Assert.Equal(["Note"], (await client.GetJsonAsync("/api/notes")).Titles());

        await client.DeleteJsonAsync($"/api/notes/{id}", HttpStatusCode.NoContent);
        await client.DeleteJsonAsync($"/api/notes/{id}?permanent=true", HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/notes/{id}")).StatusCode);
    }

    [Fact]
    public async Task Html_notes_get_html_previews()
    {
        var client = await app.NewUserClientAsync();
        var notebook = await client.CreateNotebookAsync("Inbox");
        var id = (await client.CreateNoteAsync(notebook, "Clip")).Str("id");
        // HTML notes only arrive by copying data in; set the flag directly
        await app.WithServiceAsync<Data.NotesDbContext, int>(async db =>
        {
            var note = await db.Notes.FindAsync(Guid.ParseExact(id, "N"));
            note!.MarkupLanguage = 2;
            return await db.SaveChangesAsync();
        });
        var saved = await client.PutJsonAsync($"/api/notes/{id}", new { body = "<p>Hello&nbsp;<b>world</b></p><script>x()</script>" });
        Assert.Equal("Hello world", saved.Str("preview"));
        Assert.Equal(2, saved.GetProperty("markupLanguage").GetInt32());
    }
}
