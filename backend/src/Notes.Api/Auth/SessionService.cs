using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notes.Api.Data;
using Notes.Api.Infrastructure;
using Notes.Api.Services;

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
    /// <summary>How much of the User-Agent is kept: enough to tell browsers and devices apart.</summary>
    public const int MaxUserAgentLength = 200;

    /// <summary>How often a session's last use is written back.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    private TimeSpan Lifetime => TimeSpan.FromDays(Math.Max(1, options.Value.SessionLifetimeDays));

    private TimeSpan MaxAge => TimeSpan.FromDays(Math.Max(1, options.Value.SessionMaxAgeDays));

    private DateTimeOffset ExpiryAt(DateTimeOffset now, DateTimeOffset created) =>
        now + Lifetime < created + MaxAge ? now + Lifetime : created + MaxAge;

    /// <summary>
    /// Starts a session and returns its token (to be put in the cookie) and expiry. The User-Agent
    /// only labels the session in the user's list; it isn't checked later.
    /// </summary>
    public async Task<(string Token, DateTimeOffset Expires)> CreateAsync(Guid userId, string? userAgent = null, CancellationToken ct = default)
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
            UserAgent = ShortenUserAgent(userAgent),
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

    /// <summary>The user's live sessions, most recently used first; <paramref name="currentId"/> is marked.</summary>
    public async Task<List<SessionDto>> ListAsync(Guid userId, Guid currentId, CancellationToken ct = default)
    {
        var now = Times.Now(clock);
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.ExpiresTime > now)
            .OrderByDescending(s => s.LastSeenTime)
            .ToListAsync(ct);
        return sessions.Select(s => new SessionDto(Ids.Format(s.Id), Times.ToMs(s.CreatedTime), Times.ToMs(s.LastSeenTime),
            Times.ToMs(s.ExpiresTime), s.UserAgent, s.Id == currentId)).ToList();
    }

    /// <summary>Ends one of the user's sessions; 404 for an unknown or foreign id.</summary>
    public async Task EndAsync(Guid userId, string id, CancellationToken ct = default)
    {
        var sessionId = Ids.Parse(id, "session");
        var ended = await db.Sessions.Where(s => s.UserId == userId && s.Id == sessionId).ExecuteDeleteAsync(ct);
        if (ended == 0) throw ApiException.NotFound("Session not found");
    }

    /// <summary>Ends all of the user's sessions except <paramref name="keepId"/> and returns how many there were.</summary>
    public async Task<int> EndOthersAsync(Guid userId, Guid keepId, CancellationToken ct = default) =>
        await db.Sessions.Where(s => s.UserId == userId && s.Id != keepId).ExecuteDeleteAsync(ct);

    /// <summary>The User-Agent without control characters, cut to <see cref="MaxUserAgentLength"/>.</summary>
    internal static string ShortenUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "";
        var clean = new string(userAgent.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length <= MaxUserAgentLength ? clean : clean[..MaxUserAgentLength];
    }

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
