using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Notes.Api.Auth;
using Notes.Api.Cli;

namespace Notes.Api.Tests;

public class TwoFactorTests(NotesApiFactory app) : IClassFixture<NotesApiFactory>
{
    private const string Password = "user-password";

    /// <summary>A new user with two-factor sign-in on: their client, email, secret and recovery codes.</summary>
    private async Task<(HttpClient Client, string Email, byte[] Secret, List<string> Codes)> EnrolledUserAsync()
    {
        var email = $"tfa-{Guid.NewGuid():N}@notes.test";
        var client = await app.NewUserClientAsync(email);
        var setup = await client.PostJsonAsync("/api/auth/2fa/setup", new { password = Password });
        var secret = FromBase32(setup.Str("secret"));
        var enabled = await client.PostJsonAsync("/api/auth/2fa/enable", new { code = CurrentCode(secret) });
        var codes = enabled.GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToList();
        // The next code belongs to the next step; the one just used is spent
        app.Clock.Advance(Totp.Step);
        return (client, email, secret, codes);
    }

    private string CurrentCode(byte[] secret) => Totp.Code(secret, Totp.StepAt(app.Clock.GetUtcNow()));

    private async Task<HttpResponseMessage> LoginAsync(string email, string? code, string password = Password)
    {
        using var client = app.BrowserClient();
        return await client.PostAsJsonAsync("/api/auth/login", new { email, password, code });
    }

    private static async Task<bool> AsksForSecondFactorAsync(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Unauthorized
        && (await response.ReadJsonAsync()).TryGetProperty("details", out var details)
        && details.GetProperty("secondFactor").GetBoolean();

    private static byte[] FromBase32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in text)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return [.. bytes];
    }

    [Fact]
    public async Task Setup_needs_the_password_and_a_matching_code()
    {
        using var client = await app.NewUserClientAsync();
        Assert.False((await client.GetJsonAsync("/api/auth/2fa")).GetProperty("enabled").GetBoolean());
        await client.PostJsonAsync("/api/auth/2fa/setup", new { password = "wrong-password" }, HttpStatusCode.BadRequest);
        await client.PostJsonAsync("/api/auth/2fa/enable", new { code = "123456" }, HttpStatusCode.BadRequest);

        var setup = await client.PostJsonAsync("/api/auth/2fa/setup", new { password = Password });
        Assert.Matches("^[A-Z2-7]{32}$", setup.Str("secret"));
        Assert.StartsWith("otpauth://totp/Notes:", setup.Str("uri"));
        Assert.Contains($"secret={setup.Str("secret")}&issuer=Notes", setup.Str("uri"));
        var secret = FromBase32(setup.Str("secret"));
        var wrong = Totp.Code(secret, Totp.StepAt(app.Clock.GetUtcNow()) + 5);
        await client.PostJsonAsync("/api/auth/2fa/enable", new { code = wrong }, HttpStatusCode.BadRequest);
        Assert.False((await client.GetJsonAsync("/api/auth/check")).GetProperty("user").GetProperty("twoFactor").GetBoolean());

        var enabled = await client.PostJsonAsync("/api/auth/2fa/enable", new { code = CurrentCode(secret) });
        var codes = enabled.GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, code => Assert.Matches("^[A-Z2-7]{5}-[A-Z2-7]{5}$", code));
        var status = await client.GetJsonAsync("/api/auth/2fa");
        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(10, status.GetProperty("recoveryCodesLeft").GetInt32());
        Assert.True((await client.GetJsonAsync("/api/auth/check")).GetProperty("user").GetProperty("twoFactor").GetBoolean());
        await client.PostJsonAsync("/api/auth/2fa/setup", new { password = Password }, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Sign_in_asks_for_a_code_and_accepts_each_code_once()
    {
        var (_, email, secret, codes) = await EnrolledUserAsync();

        // Without the right password there is no hint that a second factor exists
        var wrongPassword = await LoginAsync(email, null, "wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.False((await wrongPassword.ReadJsonAsync()).TryGetProperty("details", out _));
        Assert.True(await AsksForSecondFactorAsync(await LoginAsync(email, null)));
        Assert.True(await AsksForSecondFactorAsync(await LoginAsync(email, "000000")));

        var code = CurrentCode(secret);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, code[..3] + " " + code[3..])).StatusCode);
        // Replay of the same code
        Assert.True(await AsksForSecondFactorAsync(await LoginAsync(email, code)));

        // A recovery code works once, in any case and with or without the dash
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, codes[0].Replace("-", "").ToLowerInvariant())).StatusCode);
        Assert.True(await AsksForSecondFactorAsync(await LoginAsync(email, codes[0])));
        using var client = await LoginClientAsync(email, codes[1]);
        Assert.Equal(8, (await client.GetJsonAsync("/api/auth/2fa")).GetProperty("recoveryCodesLeft").GetInt32());
    }

    private async Task<HttpClient> LoginClientAsync(string email, string code)
    {
        var client = app.BrowserClient();
        (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password, code })).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Wrong_codes_count_towards_the_account_lock()
    {
        var (_, email, secret, _) = await EnrolledUserAsync();
        for (var i = 0; i < 10; i++) Assert.True(await AsksForSecondFactorAsync(await LoginAsync(email, "000000")));

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(email, CurrentCode(secret))).StatusCode);
        app.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, CurrentCode(secret))).StatusCode);
    }

    [Fact]
    public async Task New_recovery_codes_replace_the_old_ones_and_turning_off_needs_password_and_code()
    {
        var (client, email, secret, oldCodes) = await EnrolledUserAsync();
        await client.PostJsonAsync("/api/auth/2fa/recovery-codes", new { password = "wrong-password" }, HttpStatusCode.BadRequest);
        var renewed = await client.PostJsonAsync("/api/auth/2fa/recovery-codes", new { password = Password });
        var newCodes = renewed.GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.True(await AsksForSecondFactorAsync(await LoginAsync(email, oldCodes[0])));

        await client.PostJsonAsync("/api/auth/2fa/disable", new { password = Password, code = "000000" }, HttpStatusCode.BadRequest);
        await client.PostJsonAsync("/api/auth/2fa/disable", new { password = "wrong-password", code = newCodes[0] }, HttpStatusCode.BadRequest);
        await client.PostJsonAsync("/api/auth/2fa/disable", new { password = Password, code = CurrentCode(secret) }, HttpStatusCode.NoContent);

        var status = await client.GetJsonAsync("/api/auth/2fa");
        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, status.GetProperty("recoveryCodesLeft").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, null)).StatusCode);
    }

    [Fact]
    public async Task Administrators_and_the_command_line_can_turn_it_off_and_end_the_sessions()
    {
        var (client, email, _, _) = await EnrolledUserAsync();
        var id = (await client.GetJsonAsync("/api/auth/check")).GetProperty("user").Str("id");
        using var other = await app.NewUserClientAsync();
        await other.DeleteJsonAsync($"/api/admin/users/{id}/2fa", HttpStatusCode.Forbidden);

        using var admin = await app.AdminClientAsync();
        var listed = (await admin.GetJsonAsync("/api/admin/users")).EnumerateArray().Single(u => u.Str("id") == id);
        Assert.True(listed.GetProperty("twoFactor").GetBoolean());
        await admin.DeleteJsonAsync($"/api/admin/users/{id}/2fa", HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/check")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, null)).StatusCode);
        await admin.DeleteJsonAsync($"/api/admin/users/{id}/2fa", HttpStatusCode.Conflict);

        var (_, cliEmail, _, _) = await EnrolledUserAsync();
        Assert.Equal(0, await UserCommands.RunAsync(app.Services, ["users", "reset-2fa", cliEmail]));
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(cliEmail, null)).StatusCode);
        Assert.Equal(1, await UserCommands.RunAsync(app.Services, ["users", "reset-2fa", cliEmail]));
    }
}
