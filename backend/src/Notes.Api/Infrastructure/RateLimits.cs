using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Notes.Api.Infrastructure;

/// <summary>
/// Rate limits per client address and 15-minute window: <see cref="NotesOptions.RateLimitMax"/> API
/// requests, <see cref="NotesOptions.LoginRateLimitMax"/> logins. A 429 carries <c>Retry-After</c>.
/// </summary>
public static class RateLimits
{
    public const string LoginPolicy = "login";

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public static IServiceCollection AddNotesRateLimits(this IServiceCollection services, NotesOptions options)
    {
        return services.AddRateLimiter(limiter =>
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.StartsWithSegments("/api")
                    ? RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => Limit(options.RateLimitMax))
                    : RateLimitPartition.GetNoLimiter(""));
            limiter.AddPolicy(LoginPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => Limit(options.LoginRateLimitMax)));
            limiter.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;
                SecurityLog.RateLimited(http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(SecurityLog.Category),
                    ClientKey(http), http.Request.Method, http.Request.Path);
                var response = http.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }
                await response.WriteAsJsonAsync(new { error = "Too many requests, please try again later", status = 429 }, ct);
            };
        });
    }

    private static FixedWindowRateLimiterOptions Limit(int permits) => new()
    {
        PermitLimit = Math.Max(1, permits),
        Window = Window,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
