using System.Net;
using Microsoft.Extensions.Options;
using Notes.Api.Data;
using Notes.Api.Infrastructure;
using Notes.Api.Services;

namespace Notes.Api.Tests;

public class VersionTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    [Fact]
    public async Task An_editing_session_leaves_one_version_of_where_it_started()
    {
        var client = await app.NewUserClientAsync();
        var notebook = await client.CreateNotebookAsync("Inbox");
        var id = (await client.CreateNoteAsync(notebook, "Plan", "v1")).Str("id");

        // Autosaves a few seconds apart: only the first one keeps the previous content
        foreach (var body in new[] { "v2", "v3", "v4" })
        {
            app.Clock.Advance(TimeSpan.FromSeconds(5));
            await client.PutJsonAsync($"/api/notes/{id}", new { body });
        }
        var versions = await client.GetJsonAsync($"/api/notes/{id}/versions");
        var only = Assert.Single(versions.EnumerateArray());
        Assert.Equal("v1", only.Str("preview"));

        // Changes that don't touch title or body (to-do state, moving) never create versions
        await client.PutJsonAsync($"/api/notes/{id}", new { parentId = notebook });

        app.Clock.Advance(TimeSpan.FromMinutes(11));
        await client.PutJsonAsync($"/api/notes/{id}", new { body = "v5" });
        versions = await client.GetJsonAsync($"/api/notes/{id}/versions");
        Assert.Equal(["v4", "v1"], versions.EnumerateArray().Select(v => v.Str("preview")));

        var detail = await client.GetJsonAsync($"/api/notes/{id}/versions/{versions[0].Str("id")}");
        Assert.Equal("v4", detail.Str("body"));
        Assert.Equal("Plan", detail.Str("title"));
    }

    [Fact]
    public async Task Restoring_a_version_keeps_the_replaced_content_as_a_version()
    {
        var client = await app.NewUserClientAsync();
        var notebook = await client.CreateNotebookAsync("Inbox");
        var id = (await client.CreateNoteAsync(notebook, "Old title", "old body")).Str("id");
        app.Clock.Advance(TimeSpan.FromSeconds(1));
        var current = await client.PutJsonAsync($"/api/notes/{id}", new { title = "New title", body = "new body" });
        var version = (await client.GetJsonAsync($"/api/notes/{id}/versions"))[0].Str("id");

        // Stale base: conflict as for normal saves
        await client.PostJsonAsync($"/api/notes/{id}/versions/{version}/restore", new { baseUpdatedTime = 1 }, HttpStatusCode.Conflict);

        app.Clock.Advance(TimeSpan.FromSeconds(1));
        var restored = await client.PostJsonAsync($"/api/notes/{id}/versions/{version}/restore", new { baseUpdatedTime = current.Long("updatedTime") });
        Assert.Equal("Old title", restored.Str("title"));
        Assert.Equal("old body", restored.Str("body"));
        Assert.Equal("old body", restored.Str("preview"));

        var versions = await client.GetJsonAsync($"/api/notes/{id}/versions");
        Assert.Equal(["New title", "Old title"], versions.Titles());
    }

    [Fact]
    public async Task Versions_belong_to_their_note_and_owner_and_go_with_it()
    {
        var owner = await app.NewUserClientAsync();
        var other = await app.NewUserClientAsync();
        var notebook = await owner.CreateNotebookAsync("Inbox");
        var id = (await owner.CreateNoteAsync(notebook, "Secret", "v1")).Str("id");
        var second = (await owner.CreateNoteAsync(notebook, "Other")).Str("id");
        await owner.PutJsonAsync($"/api/notes/{id}", new { body = "v2" });
        var version = (await owner.GetJsonAsync($"/api/notes/{id}/versions"))[0].Str("id");

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/notes/{id}/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/notes/{id}/versions/{version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/notes/{second}/versions/{version}")).StatusCode);

        await owner.DeleteJsonAsync($"/api/notes/{id}", HttpStatusCode.NoContent);
        await owner.DeleteJsonAsync($"/api/notes/{id}?permanent=true", HttpStatusCode.NoContent);
        var left = await app.WithServiceAsync<Data.NotesDbContext, int>(db =>
            Task.FromResult(db.NoteVersions.Count(v => v.NoteId == Guid.ParseExact(id, "N"))));
        Assert.Equal(0, left);
    }

    [Fact]
    public async Task Versions_past_the_retention_period_are_pruned()
    {
        var client = await app.NewUserClientAsync();
        var notebook = await client.CreateNotebookAsync("Inbox");
        var id = (await client.CreateNoteAsync(notebook, "Diary", "v1")).Str("id");
        await client.PutJsonAsync($"/api/notes/{id}", new { body = "v2" });
        app.Clock.Advance(TimeSpan.FromDays(20));
        await client.PutJsonAsync($"/api/notes/{id}", new { body = "v3" });
        app.Clock.Advance(TimeSpan.FromDays(15));
        async Task<List<string>> PreviewsAsync() =>
            (await client.GetJsonAsync($"/api/notes/{id}/versions")).EnumerateArray().Select(v => v.Str("preview")).ToList();
        Assert.Equal(["v2", "v1"], await PreviewsAsync());

        // The default (0) keeps every version
        Assert.Equal(0, await app.WithServiceAsync<VersionService, int>(versions => versions.PruneAsync(app.Clock.GetUtcNow())));
        Assert.Equal(["v2", "v1"], await PreviewsAsync());

        // 30 days: the version from 35 days ago goes, the one from 15 days ago stays
        var deleted = await app.WithServiceAsync<NotesDbContext, int>(db =>
            new VersionService(db, Options.Create(new NotesOptions { VersionRetentionDays = 30 })).PruneAsync(app.Clock.GetUtcNow()));
        Assert.True(deleted >= 1);
        Assert.Equal(["v2"], await PreviewsAsync());
        Assert.Equal("v3", (await client.GetJsonAsync($"/api/notes/{id}")).Str("body"));
    }
}
