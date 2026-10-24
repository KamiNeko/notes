using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Notes.Api.Auth;
using Notes.Api.Data;
using Notes.Api.Infrastructure;
using Notes.Api.Services;

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

    [Fact]
    public async Task The_api_rate_limit_answers_429_with_retry_after()
    {
        using var limited = app.WithWebHostBuilder(builder => builder.UseSetting("Notes:RateLimitMax", "3"));
        using var client = limited.CreateClient();
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);

        var refused = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(429, (await refused.ReadJsonAsync()).GetProperty("status").GetInt32());
        // The static client isn't rate limited
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task The_public_url_is_an_allowed_origin_and_makes_the_cookie_secure()
    {
        using var proxied = app.WithWebHostBuilder(builder => builder.UseSetting("Notes:PublicUrl", "https://notes.example.com"));
        async Task<HttpResponseMessage> LoginFromAsync(string origin)
        {
            using var client = proxied.CreateClient();
            client.DefaultRequestHeaders.Add("Origin", origin);
            return await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@notes.test", password = "admin-password" });
        }

        // The app sees http://localhost, the browser shows the public URL (a proxy without forwarded host)
        var allowed = await LoginFromAsync("https://notes.example.com");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Contains(allowed.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-notes_session=", StringComparison.Ordinal) && c.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.Forbidden, (await LoginFromAsync("https://notes.example.com.evil.test")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await LoginFromAsync("http://notes.example.com:8443")).StatusCode);
    }

    [Fact]
    public void The_upload_limit_comes_from_max_upload_mb()
    {
        using var limited = app.WithWebHostBuilder(builder => builder.UseSetting("Notes:MaxUploadMb", "2"));
        var kestrel = limited.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>>().Value;
        Assert.Equal(2L * 1024 * 1024, kestrel.Limits.MaxRequestBodySize);
    }

    [Fact]
    public async Task Too_large_uploads_are_answered_with_413_and_a_message()
    {
        // Kestrel enforces Notes__MaxUploadMb (the test server has no body limit); the error middleware words the answer
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            NullLogger<ErrorHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("The file is too large to upload", body.RootElement.Str("error"));
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

    [Fact]
    public async Task A_password_change_that_fails_to_save_keeps_the_sessions()
    {
        using var user = await app.NewUserClientAsync();
        var id = Ids.Parse((await user.GetJsonAsync("/api/auth/check")).GetProperty("user").Str("id"), "user id");
        var services = app.Services;
        var options = new DbContextOptionsBuilder<NotesDbContext>()
            .UseNpgsql(app.ConnectionString).UseSnakeCaseNamingConvention().AddInterceptors(new FailingSaves()).Options;
        // A fresh context each time: a failed save leaves the new hash on the tracked user
        async Task FailingAsync(Func<UserService, Task> change)
        {
            await using var db = new NotesDbContext(options);
            var users = new UserService(db, app.Clock, services.GetRequiredService<IPasswordHasher<User>>(),
                new ResourceService(db, app.Clock, services.GetRequiredService<IOptions<NotesOptions>>()), services.GetRequiredService<ILoggerFactory>());
            await Assert.ThrowsAsync<InvalidOperationException>(() => change(users));
        }

        await FailingAsync(users => users.UpdateAsync("test", id, "a-new-password", null, null));
        await FailingAsync(users => users.ChangeOwnPasswordAsync(id, Guid.Empty, "user-password", "a-new-password"));

        // Neither the admin reset nor the own change ended the session without changing the password
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/auth/check")).StatusCode);
        using var again = await app.SignInAsync((await user.GetJsonAsync("/api/auth/check")).GetProperty("user").Str("email"), "user-password");
    }

    /// <summary>Fails every save, like a database that goes away mid-request.</summary>
    private sealed class FailingSaves : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated database failure");
    }

    [Fact]
    public async Task Exports_are_logged_as_a_security_event()
    {
        var log = new LogCollector();
        using var logged = app.WithWebHostBuilder(builder => builder
            .UseSetting("Logging:LogLevel:Notes.Security", "Information")
            .ConfigureLogging(logging => logging.AddProvider(log)));
        using var client = logged.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("Origin", "http://localhost");
        (await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@notes.test", password = "admin-password" })).EnsureSuccessStatusCode();
        var userId = (await client.GetJsonAsync("/api/auth/check")).GetProperty("user").Str("id");

        (await client.GetAsync("/api/export")).EnsureSuccessStatusCode();

        var exported = Assert.Single(log.Entries, e => e.EventId == 1050);
        Assert.Equal(SecurityLog.Category, exported.Category);
        Assert.Contains(userId, exported.Message);
    }

    /// <summary>Keeps every log entry in memory, for assertions on the security log.</summary>
    private sealed class LogCollector : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, int EventId, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Collector(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Collector(LogCollector owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Entries.Enqueue((category, eventId.Id, formatter(state, exception)));
        }
    }
}
