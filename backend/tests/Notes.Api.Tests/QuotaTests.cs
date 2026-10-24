using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Notes.Api.Tests;

public class QuotaTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    private const int Kb = 1024;

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, int size, string name = "file.bin")
    {
        var content = new ByteArrayContent(new byte[size]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/resources") { Content = content };
        request.Headers.Add("X-File-Name", name);
        return await client.SendAsync(request);
    }

    private static async Task<string> UserIdAsync(HttpClient client) => (await client.GetJsonAsync("/api/auth/check")).GetProperty("user").Str("id");

    private static long? QuotaBytes(JsonElement storage) =>
        storage.GetProperty("quotaBytes").ValueKind == JsonValueKind.Null ? null : storage.Long("quotaBytes");

    private int StoredFiles(string userId)
    {
        var directory = Path.Combine(app.DataDirectory, "attachments", userId);
        return Directory.Exists(directory) ? Directory.GetFiles(directory).Length : 0;
    }

    [Fact]
    public async Task Uploads_past_the_default_quota_get_413_and_leave_no_file()
    {
        using var limited = app.WithWebHostBuilder(builder => builder.UseSetting("Notes:QuotaMb", "1"));
        HttpClient Browser()
        {
            var client = limited.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            client.DefaultRequestHeaders.Add("Origin", "http://localhost");
            return client;
        }
        using var admin = Browser();
        (await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin@notes.test", password = "admin-password" })).EnsureSuccessStatusCode();
        var email = $"quota-{Guid.NewGuid():N}@notes.test";
        await admin.PostJsonAsync("/api/admin/users", new { email, password = "user-password" }, HttpStatusCode.Created);
        using var user = Browser();
        (await user.PostAsJsonAsync("/api/auth/login", new { email, password = "user-password" })).EnsureSuccessStatusCode();
        var userId = await UserIdAsync(user);

        var first = await UploadAsync(user, 600 * Kb);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var storage = await user.GetJsonAsync("/api/storage");
        Assert.Equal(600 * Kb, storage.Long("usedBytes"));
        Assert.Equal(1024 * Kb, QuotaBytes(storage));

        var refused = await UploadAsync(user, 600 * Kb);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused.StatusCode);
        var error = await refused.ReadJsonAsync();
        Assert.Equal(413, error.GetProperty("status").GetInt32());
        Assert.Contains("0.6 MB of 1 MB used", error.Str("error"));
        Assert.Equal(1024 * Kb, error.GetProperty("details").Long("quotaBytes"));
        Assert.Equal(1, StoredFiles(userId));
        Assert.Equal(600 * Kb, (await user.GetJsonAsync("/api/storage")).Long("usedBytes"));

        // What still fits is accepted, and deleting frees the space again
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(user, 400 * Kb)).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await UploadAsync(user, 100 * Kb)).StatusCode);
        await user.DeleteJsonAsync($"/api/resources/{(await first.ReadJsonAsync()).Str("id")}", HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(user, 600 * Kb)).StatusCode);
    }

    [Fact]
    public async Task Administrators_override_the_quota_per_user()
    {
        using var admin = await app.AdminClientAsync();
        var user = await app.NewUserClientAsync();
        var userId = await UserIdAsync(user);
        var unlimited = await user.GetJsonAsync("/api/storage");
        Assert.Equal(0, unlimited.Long("usedBytes"));
        Assert.Null(QuotaBytes(unlimited));

        var updated = await admin.PutJsonAsync($"/api/admin/users/{userId}", new { quotaMb = 1 });
        Assert.Equal(userId, updated.Str("id"));
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(user, 700 * Kb)).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await UploadAsync(user, 700 * Kb)).StatusCode);

        var listed = (await admin.GetJsonAsync("/api/admin/users")).EnumerateArray().Single(u => u.Str("id") == userId);
        Assert.Equal(1, listed.GetProperty("quotaMb").GetInt32());
        Assert.Equal(700 * Kb, listed.Long("usedBytes"));

        // 0 lifts the limit for this user, a reset goes back to the default (unlimited here)
        await admin.PutJsonAsync($"/api/admin/users/{userId}", new { quotaMb = 0 });
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(user, 700 * Kb)).StatusCode);
        await admin.PutJsonAsync($"/api/admin/users/{userId}", new { resetQuota = true });
        listed = (await admin.GetJsonAsync("/api/admin/users")).EnumerateArray().Single(u => u.Str("id") == userId);
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("quotaMb").ValueKind);

        await admin.PutJsonAsync($"/api/admin/users/{userId}", new { quotaMb = -1 }, HttpStatusCode.BadRequest);
        await admin.PutJsonAsync($"/api/admin/users/{userId}", new { quotaMb = 5, resetQuota = true }, HttpStatusCode.BadRequest);
        await user.PutJsonAsync($"/api/admin/users/{userId}", new { quotaMb = 0 }, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Parallel_uploads_cannot_pass_the_quota_together()
    {
        using var admin = await app.AdminClientAsync();
        var user = await app.NewUserClientAsync();
        var userId = await UserIdAsync(user);
        await admin.PutJsonAsync($"/api/admin/users/{userId}", new { quotaMb = 1 });

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => UploadAsync(user, 400 * Kb, $"part{i}.bin")));

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode));
        Assert.Equal(800 * Kb, (await user.GetJsonAsync("/api/storage")).Long("usedBytes"));
        Assert.Equal(2, StoredFiles(userId));
    }
}
