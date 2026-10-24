using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Notes.Api.Auth;

namespace Notes.Api.Tests;

public class SessionTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    private async Task<HttpClient> SignInWithAgentAsync(string email, string userAgent)
    {
        var client = app.BrowserClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "user-password" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private static List<JsonElement> Sessions(JsonElement list) => list.EnumerateArray().ToList();

    [Fact]
    public async Task The_session_list_shows_the_users_own_sessions_and_marks_this_one()
    {
        var email = $"list-{Guid.NewGuid():N}@notes.test";
        using var first = await app.NewUserClientAsync(email);
        using var phone = await SignInWithAgentAsync(email, "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Mobile/15E148 Safari/604.1");
        using var stranger = await app.NewUserClientAsync();

        var list = Sessions(await phone.GetJsonAsync("/api/auth/sessions"));
        Assert.Equal(2, list.Count);
        var current = Assert.Single(list, s => s.GetProperty("current").GetBoolean());
        Assert.Contains("iPhone", current.Str("userAgent"));
        Assert.All(list, s => Assert.True(s.Long("expiresTime") > s.Long("lastSeenTime")));
        // Another user's list doesn't show these sessions
        Assert.Single(Sessions(await stranger.GetJsonAsync("/api/auth/sessions")));
    }

    [Fact]
    public async Task A_session_can_be_ended_by_its_user_only()
    {
        var email = $"end-{Guid.NewGuid():N}@notes.test";
        using var laptop = await app.NewUserClientAsync(email);
        using var lost = await SignInWithAgentAsync(email, "LostPhone/1.0");
        using var stranger = await app.NewUserClientAsync();
        var lostId = Sessions(await lost.GetJsonAsync("/api/auth/sessions")).Single(s => s.GetProperty("current").GetBoolean()).Str("id");

        // A foreign id looks like an unknown one; a malformed id is refused
        await stranger.DeleteJsonAsync($"/api/auth/sessions/{lostId}", HttpStatusCode.NotFound);
        await laptop.DeleteJsonAsync("/api/auth/sessions/not-an-id", HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.OK, (await lost.GetAsync("/api/auth/check")).StatusCode);

        await laptop.DeleteJsonAsync($"/api/auth/sessions/{lostId}", HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.Unauthorized, (await lost.GetAsync("/api/auth/check")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync("/api/auth/check")).StatusCode);
        await laptop.DeleteJsonAsync($"/api/auth/sessions/{lostId}", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Signing_out_everywhere_else_keeps_this_session()
    {
        var email = $"others-{Guid.NewGuid():N}@notes.test";
        using var here = await app.NewUserClientAsync(email);
        using var second = await app.SignInAsync(email, "user-password");
        using var third = await app.SignInAsync(email, "user-password");

        var result = await here.DeleteJsonAsync("/api/auth/sessions");
        Assert.Equal(2, result.GetProperty("ended").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await here.GetAsync("/api/auth/check")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/check")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await third.GetAsync("/api/auth/check")).StatusCode);
        Assert.Single(Sessions(await here.GetJsonAsync("/api/auth/sessions")));
    }

    [Fact]
    public async Task Ending_the_current_session_signs_out()
    {
        using var client = await app.NewUserClientAsync();
        var id = Sessions(await client.GetJsonAsync("/api/auth/sessions")).Single().Str("id");
        var response = await client.DeleteAsync($"/api/auth/sessions/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("notes_session=;", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/check")).StatusCode);
    }

    [Fact]
    public async Task Session_routes_need_a_session_and_the_apps_origin()
    {
        var anonymous = app.BrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/auth/sessions")).StatusCode);

        using var client = await app.NewUserClientAsync();
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://evil.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("/api/auth/sessions")).StatusCode);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("  Firefox/140  ", "Firefox/140")]
    [InlineData("A\u0000B\nC", "ABC")]
    public void User_agents_are_cleaned(string? input, string expected) =>
        Assert.Equal(expected, SessionService.ShortenUserAgent(input));

    [Fact]
    public void Long_user_agents_are_cut() =>
        Assert.Equal(SessionService.MaxUserAgentLength, SessionService.ShortenUserAgent(new string('x', 1000)).Length);
}
