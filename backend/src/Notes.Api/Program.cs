using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Notes.Api.Auth;
using Notes.Api.Cli;
using Notes.Api.Data;
using Notes.Api.Endpoints;
using Notes.Api.Infrastructure;
using Notes.Api.Services;

// Runs before any configuration, so it works without the database settings
if (HealthCheck.IsCommand(args)) return await HealthCheck.RunAsync();

var builder = WebApplication.CreateBuilder(args);

var notesSection = builder.Configuration.GetSection(NotesOptions.Section);
var notesOptions = notesSection.Get<NotesOptions>() ?? new NotesOptions();
NotesOptionsValidator.ThrowIfInvalid(notesOptions);
builder.Services.Configure<NotesOptions>(notesSection);

var connectionString = builder.Configuration.GetConnectionString("Notes")
    ?? throw new InvalidOperationException("Set the database connection string ConnectionStrings__Notes");
builder.Services.AddDbContext<NotesDbContext>(options => options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

builder.Services.AddSingleton(TimeProvider.System);
// PBKDF2-HMAC-SHA512 with OWASP's recommended work factor; older hashes are upgraded on the next sign-in
builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<SessionCookies>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<NotebookService>();
builder.Services.AddScoped<NoteService>();
builder.Services.AddScoped<VersionService>();
builder.Services.AddScoped<TagService>();
builder.Services.AddScoped<SearchService>();
builder.Services.AddScoped<ResourceService>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<TrashService>();
builder.Services.AddHostedService<Housekeeping>();

builder.Services.AddAuthentication(SessionClaims.Scheme)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionClaims.Scheme, null);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(SessionClaims.AdminPolicy, policy => policy.RequireAuthenticatedUser().RequireRole("admin"));
builder.Services.AddNotesRateLimits(notesOptions);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
// Malformed bodies become BadHttpRequestException, which the error middleware answers as JSON
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = (long)Math.Max(1, notesOptions.MaxUploadMb) * 1024 * 1024;
});
if (notesOptions.TrustProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        // Forwarded headers are only believed from the proxy's addresses; anyone else could fake their client address
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var network in TrustedProxies.Parse(notesOptions.TrustedProxies)) options.KnownIPNetworks.Add(network);
    });
}

var app = builder.Build();

if (UserCommands.IsCommand(args))
{
    return await UserCommands.RunAsync(app.Services, args);
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var services = scope.ServiceProvider;
    await services.GetRequiredService<NotesDbContext>().Database.MigrateAsync();
    await Bootstrap.CreateFirstAdminAsync(services, app.Logger);
    ExportService.DeleteLeftovers(notesOptions);
    if (notesOptions.SeedDemo)
    {
        await DemoSeeder.SeedAsync(services);
        SecurityLog.DemoAccountPresent(app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(SecurityLog.Category), DemoSeeder.Email);
    }
}

if (notesOptions.TrustProxy) app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<OriginCheckMiddleware>();

// The compiled client (wwwroot): content-hashed assets are cached for good, everything else revalidated
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers[HeaderNames.CacheControl] = context.Context.Request.Path.StartsWithSegments("/assets")
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    },
});

app.UseAuthentication();
app.UseAuthorization();

app.MapApi();
app.MapFallback("/api/{**path}", () => Results.Json(new { error = "Not found", status = 404 }, statusCode: 404));
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache",
});

await app.RunAsync();
return 0;

/// <summary>Entry point; partial so the integration tests can host the app.</summary>
public partial class Program;

/// <summary>First-start setup.</summary>
internal static class Bootstrap
{
    /// <summary>Creates the administrator from <c>Notes__Admin__Email</c>/<c>Notes__Admin__Password</c> while there are no users.</summary>
    public static async Task CreateFirstAdminAsync(IServiceProvider services, ILogger logger)
    {
        var db = services.GetRequiredService<NotesDbContext>();
        if (await db.Users.AnyAsync()) return;
        var admin = services.GetRequiredService<IOptions<NotesOptions>>().Value.Admin;
        if (string.IsNullOrWhiteSpace(admin.Email) || string.IsNullOrEmpty(admin.Password))
        {
            logger.LogWarning("There are no users yet. Set Notes__Admin__Email and Notes__Admin__Password to create the first administrator.");
            return;
        }
        await services.GetRequiredService<UserService>().CreateAsync("startup (Notes__Admin)", admin.Email, admin.Password, "Admin", isAdmin: true);
        logger.LogInformation("Created the administrator {Email}", UserService.NormalizeEmail(admin.Email));
    }
}
