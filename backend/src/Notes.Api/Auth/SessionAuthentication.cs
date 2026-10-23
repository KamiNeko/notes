using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Notes.Api.Infrastructure;

namespace Notes.Api.Auth;

/// <summary>Claim types and helpers for the signed-in user.</summary>
public static class SessionClaims
{
    public const string Scheme = "Session";
    public const string SessionId = "notes:session";
    public const string AdminPolicy = "admin";

    /// <summary>The signed-in user's id. Only call on endpoints that require authorization.</summary>
    public static Guid UserId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static Guid SessionIdOf(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(SessionId)!);
}

/// <summary>
/// The session cookie. Over HTTPS it is <c>__Host-notes_session</c> (Secure, Path=/); over plain HTTP
/// (LAN setups) <c>notes_session</c>, limited to <c>/api</c>. Both are httpOnly and SameSite=Strict.
/// </summary>
public class SessionCookies(IOptions<NotesOptions> options)
{
    public const string PlainName = "notes_session";
    public const string SecureName = "__Host-notes_session";

    private bool IsSecure(HttpRequest request) => options.Value.CookieSecure.ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => request.IsHttps || (options.Value.PublicUrl?.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ?? false),
    };

    /// <summary>
    /// The token from the request: the cookie (browsers) or <c>Authorization: Bearer</c> (API clients).
    /// More than one session cookie is refused, as nobody can tell which one is meant.
    /// </summary>
    public static (string? Token, bool FromCookie) Read(HttpRequest request)
    {
        var cookies = request.Headers.Cookie.ToString().Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Count(c => c.StartsWith(PlainName + "=", StringComparison.Ordinal) || c.StartsWith(SecureName + "=", StringComparison.Ordinal));
        if (cookies > 1) return (null, true);
        var cookie = request.Cookies[SecureName] ?? request.Cookies[PlainName];
        if (!string.IsNullOrEmpty(cookie)) return (cookie, true);
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return (authorization[7..].Trim(), false);
        return (null, false);
    }

    /// <summary>Sets the cookie, clearing the other variant (the deployment may have switched between HTTP and HTTPS).</summary>
    public void Write(HttpContext context, string token, DateTimeOffset expires)
    {
        var secure = IsSecure(context.Request);
        Clear(context, keep: secure ? SecureName : PlainName);
        context.Response.Cookies.Append(secure ? SecureName : PlainName, token, Options(secure, expires));
    }

    /// <summary>Clears both cookie variants (except <paramref name="keep"/>).</summary>
    public static void Clear(HttpContext context, string? keep = null)
    {
        if (keep != PlainName) context.Response.Cookies.Delete(PlainName, Options(false, null));
        if (keep != SecureName) context.Response.Cookies.Delete(SecureName, Options(true, null));
    }

    private static CookieOptions Options(bool secure, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = secure,
        Path = secure ? "/" : "/api",
        Expires = expires,
        IsEssential = true,
    };
}

/// <summary>Authenticates requests by their session token (see <see cref="SessionService"/>).</summary>
public class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionService sessions,
    SessionCookies cookies)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var (token, fromCookie) = SessionCookies.Read(Request);
        if (string.IsNullOrEmpty(token)) return AuthenticateResult.NoResult();

        var result = await sessions.ValidateAsync(token, Context.RequestAborted);
        if (result is not { } valid) return AuthenticateResult.Fail("Invalid or expired session");
        var (session, renewed) = valid;
        if (renewed && fromCookie) cookies.Write(Context, token, session.ExpiresTime);

        var user = session.User!;
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(SessionClaims.SessionId, session.Id.ToString()),
        ];
        if (user.IsAdmin) claims.Add(new Claim(ClaimTypes.Role, "admin"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SessionClaims.Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SessionClaims.Scheme));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Response.WriteAsJsonAsync(new { error = "Unauthorized: Session has expired or is invalid", status = 401 });
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Response.WriteAsJsonAsync(new { error = "Only administrators can do this", status = 403 });
    }
}
