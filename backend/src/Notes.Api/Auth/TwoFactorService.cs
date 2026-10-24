using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Notes.Api.Data;
using Notes.Api.Infrastructure;

namespace Notes.Api.Auth;

/// <summary>
/// Optional two-factor sign-in with an authenticator app (TOTP) and one-time recovery codes. Turning it
/// on, getting new recovery codes and turning it off need the password; turning it off also needs a code.
/// </summary>
public class TwoFactorService(NotesDbContext db, TimeProvider clock, IPasswordHasher<User> hasher, ILoggerFactory loggers)
{
    /// <summary>Shown as the account's issuer in authenticator apps.</summary>
    public const string Issuer = "Notes";

    public const int RecoveryCodeCount = 10;

    private readonly ILogger log = loggers.CreateLogger(SecurityLog.Category);

    /// <summary>Whether two-factor sign-in is on, and how many recovery codes are left.</summary>
    public async Task<TwoFactorStatusDto> StatusAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        var codes = await db.RecoveryCodes.CountAsync(r => r.UserId == userId, ct);
        return new(user.TotpSecret is not null, codes);
    }

    /// <summary>Starts setup: a new secret to add to an authenticator app, confirmed with <see cref="EnableAsync"/>.</summary>
    public async Task<TwoFactorSetupDto> SetupAsync(Guid userId, string password, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        RequirePassword(user, password);
        if (user.TotpSecret is not null) throw ApiException.Conflict("Two-factor sign-in is already on");
        user.TotpPendingSecret = Totp.NewSecret();
        await db.SaveChangesAsync(ct);
        return new(Totp.ToBase32(user.TotpPendingSecret), Totp.Uri(Issuer, user.Email, user.TotpPendingSecret));
    }

    /// <summary>Turns two-factor sign-in on once a code from the new secret matches; returns the recovery codes (shown once).</summary>
    public async Task<List<string>> EnableAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        if (user.TotpSecret is not null) throw ApiException.Conflict("Two-factor sign-in is already on");
        if (user.TotpPendingSecret is null) throw ApiException.BadRequest("Start the setup first");
        var step = Totp.Match(user.TotpPendingSecret, Normalize(code), clock.GetUtcNow(), 0)
            ?? throw ApiException.BadRequest("The code doesn't match. Check the time on your phone and try the next code.");
        user.TotpSecret = user.TotpPendingSecret;
        user.TotpPendingSecret = null;
        user.TotpLastStep = step;
        user.UpdatedTime = Times.Now(clock);
        var codes = await ReplaceRecoveryCodesAsync(user, ct);
        SecurityLog.TwoFactorEnabled(log, Ids.Format(userId));
        return codes;
    }

    /// <summary>New recovery codes; the old ones stop working.</summary>
    public async Task<List<string>> NewRecoveryCodesAsync(Guid userId, string password, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        RequirePassword(user, password);
        if (user.TotpSecret is null) throw ApiException.Conflict("Two-factor sign-in is off");
        var codes = await ReplaceRecoveryCodesAsync(user, ct);
        SecurityLog.RecoveryCodesRenewed(log, Ids.Format(userId));
        return codes;
    }

    /// <summary>Turns two-factor sign-in off, with the password and a current code or a recovery code.</summary>
    public async Task DisableAsync(Guid userId, string password, string code, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        RequirePassword(user, password);
        if (user.TotpSecret is null) throw ApiException.Conflict("Two-factor sign-in is off");
        if (await VerifyAsync(user, code, ct) is null) throw ApiException.BadRequest("The code is wrong");
        await TurnOffAsync(user, ct);
        SecurityLog.TwoFactorDisabled(log, Ids.Format(userId));
    }

    /// <summary>
    /// Turns two-factor sign-in off for someone who lost their phone and recovery codes (administrators
    /// and the command line). All their sessions end. <paramref name="actor"/> names who did it, for the log.
    /// </summary>
    public async Task ResetAsync(string actor, Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        if (user.TotpSecret is null && user.TotpPendingSecret is null) throw ApiException.Conflict("Two-factor sign-in is off for this user");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await TurnOffAsync(user, ct);
        await db.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        SecurityLog.TwoFactorReset(log, actor, user.Email);
    }

    /// <summary>
    /// Checks a sign-in's second factor: a code from the app (each accepted once) or an unused recovery
    /// code (deleted on use). Null when it doesn't match.
    /// </summary>
    public async Task<SecondFactor?> VerifyAsync(User user, string code, CancellationToken ct = default)
    {
        if (user.TotpSecret is null) return null;
        var normalized = Normalize(code);
        if (Totp.Match(user.TotpSecret, normalized, clock.GetUtcNow(), user.TotpLastStep) is { } step)
        {
            // Conditional, so two sign-ins racing with the same code can't both pass
            var claimed = await db.Users.Where(u => u.Id == user.Id && u.TotpLastStep < step)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.TotpLastStep, step), ct);
            if (claimed == 0) return null;
            user.TotpLastStep = step;
            return SecondFactor.App;
        }
        if (normalized.Length == 0) return null;
        var hash = Hash(normalized);
        var used = await db.RecoveryCodes.Where(r => r.UserId == user.Id && r.CodeHash == hash).ExecuteDeleteAsync(ct);
        return used == 1 ? SecondFactor.RecoveryCode : null;
    }

    private async Task TurnOffAsync(User user, CancellationToken ct)
    {
        user.TotpSecret = null;
        user.TotpPendingSecret = null;
        user.TotpLastStep = 0;
        user.UpdatedTime = Times.Now(clock);
        await db.SaveChangesAsync(ct);
        await db.RecoveryCodes.Where(r => r.UserId == user.Id).ExecuteDeleteAsync(ct);
    }

    private async Task<List<string>> ReplaceRecoveryCodesAsync(User user, CancellationToken ct)
    {
        var now = Times.Now(clock);
        var codes = Enumerable.Range(0, RecoveryCodeCount).Select(_ => NewRecoveryCode()).ToList();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.RecoveryCodes.Where(r => r.UserId == user.Id).ExecuteDeleteAsync(ct);
        db.RecoveryCodes.AddRange(codes.Select(code => new RecoveryCode { Id = Guid.NewGuid(), UserId = user.Id, CodeHash = Hash(Normalize(code)), CreatedTime = now }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return codes;
    }

    /// <summary>Ten Base32 characters (50 bits) as <c>XXXXX-XXXXX</c>.</summary>
    private static string NewRecoveryCode()
    {
        var text = Totp.ToBase32(RandomNumberGenerator.GetBytes(7))[..10];
        return $"{text[..5]}-{text[5..]}";
    }

    /// <summary>Codes are compared without spaces and dashes, case-insensitively.</summary>
    internal static string Normalize(string? code) =>
        new((code ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant).Take(64).ToArray());

    private static byte[] Hash(string normalizedCode) => SHA256.HashData(Encoding.UTF8.GetBytes(normalizedCode));

    private void RequirePassword(User user, string password)
    {
        if (password.Length > UserService.MaxPasswordLength || hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            throw ApiException.BadRequest("The password is wrong");
        }
    }

    private async Task<User> RequireUserAsync(Guid userId, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw ApiException.NotFound("User not found");
}

/// <summary>Which second factor a sign-in used.</summary>
public enum SecondFactor
{
    App,
    RecoveryCode,
}

public record TwoFactorStatusDto(bool Enabled, int RecoveryCodesLeft);

public record TwoFactorSetupDto(string Secret, string Uri);
