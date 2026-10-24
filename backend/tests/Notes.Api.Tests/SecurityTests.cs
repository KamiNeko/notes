using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Notes.Api.Infrastructure;

namespace Notes.Api.Tests;

public class SecurityTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    [Fact]
    public async Task An_account_is_locked_after_too_many_failed_sign_ins_from_any_address()
    {
        var email = $"locked-{Guid.NewGuid():N}@notes.test";
        using (await app.NewUserClientAsync(email)) { }
        var client = app.BrowserClient();
        for (var i = 0; i < 10; i++)
        {
            var failed = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        // Even the right password is refused while locked, and the answer says how long to wait
        var locked = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "user-password" });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.NotNull(locked.Headers.RetryAfter);
        // Other accounts are not affected
        using (await app.AdminClientAsync()) { }

        app.Clock.Advance(TimeSpan.FromMinutes(16));
        using (await app.SignInAsync(email, "user-password")) { }
    }

    [Fact]
    public async Task Unknown_emails_are_answered_like_wrong_passwords()
    {
        var client = app.BrowserClient();
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@notes.test", password = "some-password" });
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@notes.test", password = "some-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal((await wrong.ReadJsonAsync()).Str("error"), (await unknown.ReadJsonAsync()).Str("error"));
    }

    [Fact]
    public async Task A_session_ends_after_its_maximum_age_even_when_used()
    {
        var client = await app.NewUserClientAsync();
        for (var day = 0; day < 85; day += 17)
        {
            app.Clock.Advance(TimeSpan.FromDays(17));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/check")).StatusCode);
        }
        app.Clock.Advance(TimeSpan.FromDays(6));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/check")).StatusCode);
    }

    [Fact]
    public async Task Passwords_need_twelve_characters_and_overlong_ones_are_refused()
    {
        using var admin = await app.AdminClientAsync();
        await admin.PostJsonAsync("/api/admin/users", new { email = "short@notes.test", password = "eleven-char" }, HttpStatusCode.BadRequest);
        await admin.PostJsonAsync("/api/admin/users", new { email = "long@notes.test", password = new string('x', 1025) }, HttpStatusCode.BadRequest);
        await admin.PostJsonAsync("/api/admin/users", new { email = "ok@notes.test", password = "twelve-chars" }, HttpStatusCode.Created);

        var overlong = await app.BrowserClient().PostAsJsonAsync("/api/auth/login", new { email = "ok@notes.test", password = new string('x', 5000) });
        Assert.Equal(HttpStatusCode.Unauthorized, overlong.StatusCode);
    }

    [Fact]
    public async Task Responses_carry_the_security_headers_and_api_answers_are_not_cached()
    {
        using var client = await app.NewUserClientAsync();
        var response = await client.GetAsync("/api/notebooks");
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("script-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("camera=()", response.Headers.GetValues("Permissions-Policy").Single());
        // Plain HTTP: no HSTS (it would lock out a LAN setup without TLS), and no server banner
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Forwarded_headers_are_ignored_unless_a_proxy_is_trusted()
    {
        // TrustProxy is off in the tests: a client can't pretend to come over HTTPS or from elsewhere
        var client = app.BrowserClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@notes.test", password = "admin-password" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        // The session cookie is the plain-HTTP one (the Secure variant is only cleared)
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("notes_session=", StringComparison.Ordinal) && !c.StartsWith("notes_session=;", StringComparison.Ordinal));
        Assert.DoesNotContain(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-notes_session=", StringComparison.Ordinal) && !c.StartsWith("__Host-notes_session=;", StringComparison.Ordinal));
    }

    [Fact]
    public void Trusted_proxies_default_to_private_networks_and_reject_garbage()
    {
        var defaults = Infrastructure.TrustedProxies.Parse(null);
        Assert.Contains(defaults, n => n.Contains(IPAddress.Parse("172.18.0.2")));
        Assert.DoesNotContain(defaults, n => n.Contains(IPAddress.Parse("203.0.113.7")));
        var custom = Infrastructure.TrustedProxies.Parse("203.0.113.7, 2001:db8::/32");
        Assert.Contains(custom, n => n.Contains(IPAddress.Parse("203.0.113.7")));
        Assert.Throws<InvalidOperationException>(() => Infrastructure.TrustedProxies.Parse("not-an-address"));
    }

    [Theory]
    [InlineData("Notes:CookieSecure", "yes", "Notes__CookieSecure")]
    [InlineData("Notes:PublicUrl", "notes.example.com", "Notes__PublicUrl")]
    [InlineData("Notes:SessionLifetimeDays", "0", "Notes__SessionLifetimeDays")]
    [InlineData("Notes:LoginMaxFailures", "-1", "Notes__LoginMaxFailures")]
    public void Invalid_settings_stop_the_start_with_a_message_naming_them(string key, string value, string named)
    {
        using var broken = app.WithWebHostBuilder(builder => builder.UseSetting(key, value));
        var error = Assert.Throws<InvalidOperationException>(() => broken.CreateClient());
        Assert.Contains(named, error.Message);
    }

    [Fact]
    public void Valid_settings_pass_and_every_problem_is_listed()
    {
        Assert.Empty(NotesOptionsValidator.Validate(new NotesOptions()));
        Assert.Empty(NotesOptionsValidator.Validate(new NotesOptions { CookieSecure = "True", PublicUrl = "https://notes.example.com", TrustProxy = true, TrustedProxies = "10.0.0.1" }));
        var errors = NotesOptionsValidator.Validate(new NotesOptions
        {
            PublicUrl = "ftp://notes.example.com", MaxUploadMb = 0, VersionRetentionDays = -1, TrustProxy = true, TrustedProxies = "proxy",
        });
        Assert.Equal(4, errors.Count);
    }
}
