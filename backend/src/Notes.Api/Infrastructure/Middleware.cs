using Microsoft.Extensions.Options;
using Notes.Api.Auth;

namespace Notes.Api.Infrastructure;

/// <summary>
/// Turns exceptions into JSON <c>{ error, status }</c> responses. Only messages meant for the client
/// are sent; unexpected errors are logged and answered with a generic 500.
/// </summary>
public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception error) when (!context.Response.HasStarted)
        {
            var (status, message, details) = error switch
            {
                ApiException api => (api.Status, api.Message, api.Details),
                BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => (413, "The file is too large to upload", null),
                BadHttpRequestException bad => (bad.StatusCode, "Invalid request", null),
                OperationCanceledException when context.RequestAborted.IsCancellationRequested => (499, "Request cancelled", null),
                _ => (500, "An unexpected internal error occurred", (object?)null),
            };
            if (status >= 500) logger.LogError(error, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.Clear();
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(details is null
                ? new Dictionary<string, object> { ["error"] = message, ["status"] = status }
                : new Dictionary<string, object> { ["error"] = message, ["status"] = status, ["details"] = details });
        }
    }
}

/// <summary>
/// CSRF protection for writes (POST/PUT/PATCH/DELETE under <c>/api</c>), on top of the SameSite=Strict
/// cookie: a browser's <c>Origin</c> must be the app's own origin (the request's host, or
/// <see cref="NotesOptions.PublicUrl"/>). Requests without <c>Origin</c> (non-browser clients) pass
/// only when they don't carry the session cookie.
/// </summary>
public class OriginCheckMiddleware(RequestDelegate next, IOptions<NotesOptions> options, ILoggerFactory loggers)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method))
        {
            var origin = request.Headers.Origin.ToString();
            var allowed = origin.Length == 0
                ? !SessionCookies.Read(request).FromCookie
                : IsAllowed(origin, request);
            if (!allowed)
            {
                SecurityLog.OriginRejected(loggers.CreateLogger(SecurityLog.Category), request.Method, request.Path, SecurityLog.Client(context), origin.Length == 0 ? "(none)" : origin);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Request origin not allowed", status = 403 });
                return;
            }
        }
        await next(context);
    }

    private bool IsAllowed(string origin, HttpRequest request)
    {
        if (string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase)) return true;
        // Behind a proxy that doesn't forward the scheme, accept the host with either scheme
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)) return true;
        var publicUrl = options.Value.PublicUrl;
        return Uri.TryCreate(publicUrl, UriKind.Absolute, out var configured)
            && string.Equals(origin, configured.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Security headers for every response. <c>script-src</c> must stay <c>'self'</c>: attachment
/// <c>blob:</c> URLs opened directly run in the app's origin, and the CSP keeps their scripts from running.
/// HSTS is sent over HTTPS only (behind a proxy that needs <see cref="NotesOptions.TrustProxy"/>), and
/// API answers are <c>no-store</c> so notes don't stay in the cache of a shared computer.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next, IOptions<NotesOptions> options)
{
    public Task InvokeAsync(HttpContext context)
    {
        var o = options.Value;
        var images = o.AllowRemoteImages ? "'self' data: blob: https:" : "'self' data: blob:";
        var upgrade = o.PublicUrl?.StartsWith("https:", StringComparison.OrdinalIgnoreCase) == true ? "; upgrade-insecure-requests" : "";
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy =
            $"default-src 'self'; base-uri 'self'; font-src 'self' data:; form-action 'self'; frame-ancestors 'none'; " +
            $"img-src {images}; media-src 'self' blob:; object-src 'none'; script-src 'self'; script-src-attr 'none'; " +
            $"style-src 'self' 'unsafe-inline'; worker-src 'self' blob:{upgrade}";
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";
        if (context.Request.IsHttps) headers.StrictTransportSecurity = "max-age=31536000";
        // Endpoints that may be cached (attachments) replace this
        if (context.Request.Path.StartsWithSegments("/api")) headers.CacheControl = "no-store";
        return next(context);
    }
}
