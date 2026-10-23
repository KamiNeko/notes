using System.Net;
using System.Text.Json;

namespace Notes.Api.Tests;

public class NotebookTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    private static JsonElement Find(JsonElement tree, string id)
    {
        foreach (var node in tree.EnumerateArray())
        {
            if (node.Str("id") == id) return node;
            var child = Find(node.GetProperty("children"), id);
            if (child.ValueKind != JsonValueKind.Undefined) return child;
        }
        return default;
    }

    [Fact]
    public async Task Tree_is_nested_sorted_and_counts_active_notes()
    {
        var client = await app.NewUserClientAsync();
        var work = await client.CreateNotebookAsync("Work");
        await client.CreateNotebookAsync("archive");
        var projects = await client.CreateNotebookAsync("Projects", work);
        await client.CreateNoteAsync(projects, "One");
        var trashed = (await client.CreateNoteAsync(projects, "Two")).Str("id");
        await client.DeleteJsonAsync($"/api/notes/{trashed}", HttpStatusCode.NoContent);

        var tree = await client.GetJsonAsync("/api/notebooks");
        Assert.Equal(["archive", "Work"], tree.Titles());
        var node = Find(tree, projects);
        Assert.Equal(work, node.Str("parentId"));
        Assert.Equal(1, node.GetProperty("noteCount").GetInt32());
    }

    [Fact]
    public async Task Rename_and_move_and_refuse_cycles()
    {
        var client = await app.NewUserClientAsync();
        var a = await client.CreateNotebookAsync("A");
        var b = await client.CreateNotebookAsync("B", a);

        var renamed = await client.PutJsonAsync($"/api/notebooks/{a}", new { title = "  Renamed\n" });
        Assert.Equal("Renamed", renamed.Str("title"));
        await client.PutJsonAsync($"/api/notebooks/{a}", new { title = " " }, HttpStatusCode.BadRequest);
        await client.PutJsonAsync($"/api/notebooks/{a}", new { parentId = b }, HttpStatusCode.BadRequest);
        await client.PutJsonAsync($"/api/notebooks/{a}", new { parentId = a }, HttpStatusCode.BadRequest);

        var moved = await client.PutJsonAsync($"/api/notebooks/{b}", new { parentId = "" });
        Assert.Equal("", moved.Str("parentId"));
        Assert.Equal(["B", "Renamed"], (await client.GetJsonAsync("/api/notebooks")).Titles());
    }

    [Fact]
    public async Task Invalid_ids_are_400_and_unknown_ones_404()
    {
        var client = await app.NewUserClientAsync();
        await client.PutJsonAsync("/api/notebooks/../etc", new { title = "x" }, HttpStatusCode.NotFound);
        await client.PutJsonAsync("/api/notebooks/not-an-id", new { title = "x" }, HttpStatusCode.BadRequest);
        await client.PutJsonAsync($"/api/notebooks/{Guid.NewGuid():N}", new { title = "x" }, HttpStatusCode.NotFound);
        await client.PostJsonAsync("/api/notebooks", new { title = "x", parentId = Guid.NewGuid().ToString("N") }, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Trash_restore_brings_back_what_was_trashed_together()
    {
        var client = await app.NewUserClientAsync();
        var parent = await client.CreateNotebookAsync("Parent");
        var child = await client.CreateNotebookAsync("Child", parent);
        var kept = (await client.CreateNoteAsync(child, "Kept")).Str("id");
        var earlier = (await client.CreateNoteAsync(child, "Trashed earlier")).Str("id");
        await client.DeleteJsonAsync($"/api/notes/{earlier}", HttpStatusCode.NoContent);
        app.Clock.Advance(TimeSpan.FromSeconds(5));

        var trashed = await client.DeleteJsonAsync($"/api/notebooks/{parent}");
        Assert.Equal(2, trashed.GetProperty("notebooks").GetInt32());
        Assert.Equal(1, trashed.GetProperty("notes").GetInt32());
        Assert.Equal(0, (await client.GetJsonAsync("/api/notebooks")).GetArrayLength());
        Assert.Equal(2, (await client.GetJsonAsync("/api/notes?is_trash=true")).GetArrayLength());

        var trash = Assert.Single((await client.GetJsonAsync("/api/notebooks/trash")).EnumerateArray());
        Assert.Equal(parent, trash.Str("id"));
        Assert.Equal(1, trash.GetProperty("noteCount").GetInt32());
        Assert.Equal(1, trash.GetProperty("notebookCount").GetInt32());
        Assert.Equal(1, trash.GetProperty("earlierNoteCount").GetInt32());

        // Creating in a trashed notebook is refused
        await client.PostJsonAsync("/api/notes", new { parentId = child, title = "x" }, HttpStatusCode.NotFound);

        await client.PostJsonAsync($"/api/notebooks/{parent}/restore");
        Assert.Equal(["Kept"], (await client.GetJsonAsync($"/api/notes?notebookId={child}")).Titles());
        Assert.Equal(["Trashed earlier"], (await client.GetJsonAsync("/api/notes?is_trash=true")).Titles());
        Assert.Equal(kept, (await client.GetJsonAsync($"/api/notes/{kept}")).Str("id"));
        await client.PostJsonAsync($"/api/notebooks/{parent}/restore", null, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Restoring_a_note_restores_its_trashed_notebooks()
    {
        var client = await app.NewUserClientAsync();
        var parent = await client.CreateNotebookAsync("Parent");
        var child = await client.CreateNotebookAsync("Child", parent);
        var note = (await client.CreateNoteAsync(child, "Note")).Str("id");
        await client.DeleteJsonAsync($"/api/notebooks/{parent}");

        var restored = await client.PostJsonAsync($"/api/notes/{note}/restore");
        Assert.Equal(0, restored.Long("deletedTime"));
        var tree = await client.GetJsonAsync("/api/notebooks");
        Assert.Equal(["Parent"], tree.Titles());
        Assert.Equal(["Child"], tree[0].GetProperty("children").Titles());
    }

    [Fact]
    public async Task Purge_deletes_a_trashed_notebook_with_everything_in_it()
    {
        var client = await app.NewUserClientAsync();
        var parent = await client.CreateNotebookAsync("Parent");
        var child = await client.CreateNotebookAsync("Child", parent);
        var note = (await client.CreateNoteAsync(child, "Note")).Str("id");

        await client.DeleteJsonAsync($"/api/notebooks/{parent}?permanent=true", HttpStatusCode.Conflict);
        await client.DeleteJsonAsync($"/api/notebooks/{parent}");
        var purged = await client.DeleteJsonAsync($"/api/notebooks/{parent}?permanent=true");
        Assert.Equal(2, purged.GetProperty("notebooks").GetInt32());
        Assert.Equal(1, purged.GetProperty("notes").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/notes/{note}")).StatusCode);
        Assert.Equal(0, (await client.GetJsonAsync("/api/notebooks/trash")).GetArrayLength());
    }
}
