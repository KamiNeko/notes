using System.Net;
using System.Net.Http.Json;

namespace Notes.Api.Tests;

public class AuthTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    [Fact]
    public async Task Login_with_wrong_password_is_401()
    {
        var client = app.BrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@notes.test", password = "nope-nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid email or password", (await response.ReadJsonAsync()).Str("error"));
    }

    [Fact]
    public async Task Login_sets_an_httponly_cookie_and_check_returns_the_user()
    {
        var client = app.BrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = " Admin@Notes.test ", password = "admin-password" });
        response.EnsureSuccessStatusCode();
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("notes_session="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        // Browsers (requests with Origin) never get the token in the body
        Assert.False((await response.ReadJsonAsync()).TryGetProperty("sessionId", out _));

        var check = await client.GetJsonAsync("/api/auth/check");
        Assert.True(check.GetProperty("valid").GetBoolean());
        Assert.Equal("admin@notes.test", check.GetProperty("user").Str("email"));
        Assert.True(check.GetProperty("user").GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task Api_clients_can_use_a_bearer_token()
    {
        var client = app.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@notes.test", password = "admin-password" });
        var token = (await login.ReadJsonAsync()).Str("sessionId");

        var api = app.CreateClient();
        api.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var notebook = await api.CreateNotebookAsync("From a script");
        Assert.Equal(32, notebook.Length);
    }

    [Fact]
    public async Task Requests_without_a_session_are_401()
    {
        var client = app.BrowserClient();
        var response = await client.GetAsync("/api/notes");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(401, (await response.ReadJsonAsync()).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Logout_ends_the_session_on_the_server()
    {
        var client = await app.AdminClientAsync();
        await client.PostJsonAsync("/api/auth/logout");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/check")).StatusCode);
    }

    [Fact]
    public async Task Sessions_expire_after_the_lifetime_without_use()
    {
        var client = await app.NewUserClientAsync();
        app.Clock.Advance(TimeSpan.FromDays(29));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/check")).StatusCode);
        // Using it moved the expiry ahead
        app.Clock.Advance(TimeSpan.FromDays(29));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/check")).StatusCode);
        app.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/check")).StatusCode);
    }

    [Fact]
    public async Task Writes_from_another_origin_or_without_origin_are_refused()
    {
        var client = await app.AdminClientAsync();
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        var foreign = await client.PostAsJsonAsync("/api/notebooks", new { title = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        client.DefaultRequestHeaders.Remove("Origin");
        var missing = await client.PostAsJsonAsync("/api/notebooks", new { title = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_needs_the_current_one_and_ends_other_sessions()
    {
        var email = $"pw-{Guid.NewGuid():N}@notes.test";
        var first = await app.NewUserClientAsync(email);
        var second = await app.SignInAsync(email, "user-password");

        await first.PostJsonAsync("/api/auth/password", new { currentPassword = "wrong-one", newPassword = "brand-new-password" }, HttpStatusCode.BadRequest);
        await first.PostJsonAsync("/api/auth/password", new { currentPassword = "user-password", newPassword = "short" }, HttpStatusCode.BadRequest);
        await first.PostJsonAsync("/api/auth/password", new { currentPassword = "user-password", newPassword = "brand-new-password" }, HttpStatusCode.NoContent);

        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/auth/check")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/check")).StatusCode);
        await app.SignInAsync(email, "brand-new-password");
    }

    [Fact]
    public async Task Malformed_json_is_a_400_with_a_message()
    {
        var client = await app.AdminClientAsync();
        var response = await client.PostAsync("/api/notebooks", new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Invalid request", (await response.ReadJsonAsync()).Str("error"));
    }

    [Fact]
    public async Task Health_needs_no_session_and_responses_carry_security_headers()
    {
        var response = await app.CreateClient().GetAsync("/api/health");
        response.EnsureSuccessStatusCode();
        var csp = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }
}
