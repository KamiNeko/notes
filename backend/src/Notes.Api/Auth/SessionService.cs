using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notes.Api.Data;
using Notes.Api.Infrastructure;

namespace Notes.Api.Auth;

/// <summary>
/// Server-side sessions. The browser gets a random token in an httpOnly cookie; the database keeps
/// only its SHA-256 hash, so a leaked database can't be used to sign in. Sessions slide: each use
/// (at most every few minutes) moves the expiry <see cref="NotesOptions.SessionLifetimeDays"/> ahead,
/// but never past <see cref="NotesOptions.SessionMaxAgeDays"/> after sign-in, so a stolen token
/// stops working eventually even when it is used.
/// </summary>
public class SessionService(NotesDbContext db, TimeProvider clock, IOptions<NotesOptions> options)
{
    /// <summary>How often a session's last use is written back.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    private TimeSpan Lifetime => TimeSpan.FromDays(Math.Max(1, options.Value.SessionLifetimeDays));

    private TimeSpan MaxAge => TimeSpan.FromDays(Math.Max(1, options.Value.SessionMaxAgeDays));

    private DateTimeOffset ExpiryAt(DateTimeOffset now, DateTimeOffset created) =>
        now + Lifetime < created + MaxAge ? now + Lifetime : created + MaxAge;

    /// <summary>Starts a session and returns its token (to be put in the cookie) and expiry.</summary>
    public async Task<(string Token, DateTimeOffset Expires)> CreateAsync(Guid userId, CancellationToken ct = default)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var now = Times.Now(clock);
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Hash(token),
            CreatedTime = now,
            LastSeenTime = now,
            ExpiresTime = ExpiryAt(now, now),
        };
        db.Sessions.Add(session);
        // Expired sessions are cleaned up whenever someone signs in
        await db.Sessions.Where(s => s.ExpiresTime < now).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        return (token, session.ExpiresTime);
    }

    /// <summary>
    /// The live session for a token, with its user, or null. <c>Renewed</c> is set when the expiry
    /// moved, so the caller can refresh the cookie.
    /// </summary>
    public async Task<(Session Session, bool Renewed)?> ValidateAsync(string token, CancellationToken ct = default)
    {
        if (token.Length is < 20 or > 100) return null;
        var hash = Hash(token);
        var session = await db.Sessions.Include(s => s.User).FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        var now = Times.Now(clock);
        if (session?.User is null || session.ExpiresTime <= now) return null;
        if (now - session.LastSeenTime < TouchInterval) return (session, false);
        session.LastSeenTime = now;
        session.ExpiresTime = ExpiryAt(now, session.CreatedTime);
        await db.SaveChangesAsync(ct);
        return (session, true);
    }

    /// <summary>Ends the session with this token, if any, and returns its user's id.</summary>
    public async Task<Guid?> RevokeAsync(string token, CancellationToken ct = default)
    {
        if (token.Length is < 20 or > 100) return null;
        var hash = Hash(token);
        var userId = await db.Sessions.Where(s => s.TokenHash == hash).Select(s => (Guid?)s.UserId).FirstOrDefaultAsync(ct);
        if (userId is not null) await db.Sessions.Where(s => s.TokenHash == hash).ExecuteDeleteAsync(ct);
        return userId;
    }

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
