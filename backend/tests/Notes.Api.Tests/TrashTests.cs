using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Notes.Api.Data;
using Notes.Api.Infrastructure;
using Notes.Api.Services;

namespace Notes.Api.Tests;

public class TrashTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    private Task<CountsDto> EmptyExpiredAsync(int retentionDays) =>
        app.WithServiceAsync<NotesDbContext, CountsDto>(db =>
        {
            var options = Options.Create(new NotesOptions { TrashRetentionDays = retentionDays });
            return new TrashService(db, new NotebookService(db, app.Clock), options).EmptyExpiredAsync(app.Clock.GetUtcNow());
        });

    [Fact]
    public async Task Trash_older_than_the_retention_period_is_emptied()
    {
        var client = await app.NewUserClientAsync();
        var inbox = await client.CreateNotebookAsync("Inbox");
        var oldNote = (await client.CreateNoteAsync(inbox, "Old", "gone soon")).Str("id");
        var oldVersioned = (await client.CreateNoteAsync(inbox, "Versioned", "v1")).Str("id");
        await client.PutJsonAsync($"/api/notes/{oldVersioned}", new { body = "v2" });
        var archive = await client.CreateNotebookAsync("Archive");
        var nested = await client.CreateNotebookAsync("Nested", archive);
        await client.CreateNoteAsync(nested, "Inside", "deep");
        await client.DeleteJsonAsync($"/api/notes/{oldNote}", HttpStatusCode.NoContent);
        await client.DeleteJsonAsync($"/api/notes/{oldVersioned}", HttpStatusCode.NoContent);
        await client.DeleteJsonAsync($"/api/notebooks/{archive}");

        app.Clock.Advance(TimeSpan.FromDays(20));
        var recent = (await client.CreateNoteAsync(inbox, "Recent", "stays")).Str("id");
        await client.DeleteJsonAsync($"/api/notes/{recent}", HttpStatusCode.NoContent);
        var active = (await client.CreateNoteAsync(inbox, "Active", "untouched")).Str("id");
        app.Clock.Advance(TimeSpan.FromDays(11));

        // The default (0) keeps the trash until the user empties it
        Assert.Equal(new CountsDto(0, 0), await EmptyExpiredAsync(0));

        var deleted = await EmptyExpiredAsync(30);
        Assert.True(deleted.Notebooks >= 2);
        Assert.True(deleted.Notes >= 3);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/notes/{oldNote}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/notes/{oldVersioned}")).StatusCode);
        Assert.Empty((await client.GetJsonAsync("/api/notebooks/trash")).EnumerateArray());
        // Trashed 11 days ago, and never trashed: both stay
        Assert.Equal("Recent", (await client.GetJsonAsync($"/api/notes/{recent}")).Str("title"));
        Assert.Equal("Active", (await client.GetJsonAsync($"/api/notes/{active}")).Str("title"));
        Assert.Equal(["Recent"], (await client.GetJsonAsync("/api/notes?isTrash=true")).Titles());
    }

    [Fact]
    public async Task The_client_learns_the_retention_period_from_the_settings()
    {
        using var client = await app.NewUserClientAsync();
        Assert.Equal(0, (await client.GetJsonAsync("/api/settings")).GetProperty("trashRetentionDays").GetInt32());

        using var configured = app.WithWebHostBuilder(builder => builder.UseSetting("Notes:TrashRetentionDays", "30"));
        var admin = configured.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        admin.DefaultRequestHeaders.Add("Origin", "http://localhost");
        (await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin@notes.test", password = "admin-password" })).EnsureSuccessStatusCode();
        Assert.Equal(30, (await admin.GetJsonAsync("/api/settings")).GetProperty("trashRetentionDays").GetInt32());

        Assert.Equal(HttpStatusCode.Unauthorized, (await app.BrowserClient().GetAsync("/api/settings")).StatusCode);
    }
}
