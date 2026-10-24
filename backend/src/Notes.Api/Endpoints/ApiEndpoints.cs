using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Notes.Api.Auth;
using Notes.Api.Infrastructure;
using Notes.Api.Services;

namespace Notes.Api.Endpoints;

public record LoginRequest(string? Email, string? Password);

public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public record NotebookRequest(string? Title, string? ParentId);

public record CreateNoteRequest(string? Title, string? Body, string? ParentId, bool? IsTodo);

public record UpdateNoteRequest(string? Title, string? Body, string? ParentId, bool? TodoCompleted, long? TodoDue, long? BaseUpdatedTime, bool? Force);

public record TagRequest(string? Title);

public record RestoreVersionRequest(long? BaseUpdatedTime);

public record CreateUserRequest(string? Email, string? Password, string? DisplayName, bool? IsAdmin);

public record UpdateUserRequest(string? Password, string? DisplayName, bool? IsAdmin, int? QuotaMb, bool? ResetQuota);

/// <summary>Maps the REST API under <c>/api</c>. Handlers stay thin; the services do the work.</summary>
public static class ApiEndpoints
{
    /// <summary>Latest time a <see cref="DateTimeOffset"/> can hold, in Unix ms.</summary>
    private static readonly long MaxDateMs = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", () =>
        {
            var version = typeof(ApiEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
            return Results.Ok(new { status = "ok", version = version.Split('+')[0], commit = Environment.GetEnvironmentVariable("GIT_SHA") ?? "" });
        });

        MapAuth(api);

        var authed = api.MapGroup("").RequireAuthorization();
        MapNotebooks(authed.MapGroup("/notebooks"));
        MapNotes(authed.MapGroup("/notes"));
        MapTags(authed.MapGroup("/tags"));
        authed.MapGet("/search", async (string? q, string? query, ClaimsPrincipal user, SearchService search, CancellationToken ct) =>
        {
            var text = q ?? query ?? "";
            return string.IsNullOrWhiteSpace(text) ? Results.Ok(Array.Empty<object>()) : Results.Ok(await search.SearchAsync(user.UserId(), text, ct));
        });
        MapResources(authed.MapGroup("/resources"));
        // Server settings the client shows to the user (only what the UI needs, nothing about the deployment)
        authed.MapGet("/settings", (IOptions<NotesOptions> options) => Results.Ok(new SettingsDto(Math.Max(0, options.Value.TrashRetentionDays))));
        authed.MapGet("/storage", async (ClaimsPrincipal user, ResourceService resources, CancellationToken ct) =>
            Results.Ok(await resources.StorageAsync(user.UserId(), ct)));
        // All of the user's notes as a ZIP of Markdown files (CRA: users can take their data elsewhere)
        authed.MapGet("/export", async (HttpContext context, ClaimsPrincipal user, ExportService export, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var zip = await export.CreateAsync(user.UserId(), ct);
            // Everything an account holds in one download: the first thing to look for after a stolen session
            SecurityLog.DataExported(loggers.CreateLogger(SecurityLog.Category), Ids.Format(user.UserId()), SecurityLog.Client(context));
            return Results.Stream(zip, "application/zip", $"notes-export-{clock.GetUtcNow():yyyy-MM-dd}.zip");
        });
        // A ZIP of Markdown files (the export's format) into a new notebook; the body is the raw ZIP
        authed.MapPost("/import", async (HttpContext context, ClaimsPrincipal user, ImportService import, IOptions<NotesOptions> options,
            ILoggerFactory loggers, CancellationToken ct) =>
        {
            // Larger than an attachment: the ZIP holds all of them
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = (long)options.Value.MaxImportMb * 1024 * 1024;
            }
            var result = await import.ImportAsync(user.UserId(), context.Request.Body, ct);
            SecurityLog.DataImported(loggers.CreateLogger(SecurityLog.Category), Ids.Format(user.UserId()), result.Notes, result.Attachments, SecurityLog.Client(context));
            return Results.Created((string?)null, result);
        });
        MapAdmin(api.MapGroup("/admin").RequireAuthorization(SessionClaims.AdminPolicy));
    }

    private static void MapAuth(RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");

        auth.MapPost("/login", async (LoginRequest body, HttpContext context, UserService users, SessionService sessions, SessionCookies cookies,
            LoginThrottle throttle, ILoggerFactory loggers, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(body.Email) || string.IsNullOrEmpty(body.Password)) throw ApiException.BadRequest("Email and password are required");
            var log = loggers.CreateLogger(SecurityLog.Category);
            var email = UserService.NormalizeEmail(body.Email);
            var client = SecurityLog.Client(context);
            if (throttle.LockedFor(email) is { } locked)
            {
                SecurityLog.LoginLocked(log, email, client);
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(locked.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                return Results.Json(new { error = "Too many failed sign-ins for this account. Please try again later.", status = 429 },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }
            var user = await users.VerifyCredentialsAsync(email, body.Password, ct);
            if (user is null)
            {
                throttle.RecordFailure(email);
                SecurityLog.LoginFailed(log, email, client);
                throw new ApiException(StatusCodes.Status401Unauthorized, "Invalid email or password");
            }
            throttle.Reset(email);
            var (token, expires) = await sessions.CreateAsync(user.Id, context.Request.Headers.UserAgent.ToString(), ct);
            cookies.Write(context, token, expires);
            SecurityLog.LoginSucceeded(log, user.Email, client);
            // Only non-browser API clients (no Origin header) get the token in the body; browsers only ever get the httpOnly cookie
            return Results.Ok(context.Request.Headers.ContainsKey(HeaderNames.Origin)
                ? new Dictionary<string, object> { ["userId"] = Ids.Format(user.Id), ["user"] = UserService.ToDto(user) }
                : new Dictionary<string, object> { ["userId"] = Ids.Format(user.Id), ["user"] = UserService.ToDto(user), ["sessionId"] = token });
        }).RequireRateLimiting(RateLimits.LoginPolicy);

        auth.MapPost("/logout", async (HttpContext context, SessionService sessions, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var (token, _) = SessionCookies.Read(context.Request);
            SessionCookies.Clear(context);
            if (!string.IsNullOrEmpty(token) && await sessions.RevokeAsync(token, ct) is { } userId)
            {
                SecurityLog.LoggedOut(loggers.CreateLogger(SecurityLog.Category), Ids.Format(userId));
            }
            return Results.Ok(new { message = "Successfully logged out" });
        });

        auth.MapGet("/check", async (ClaimsPrincipal principal, UserService users, CancellationToken ct) =>
        {
            var user = await users.FindAsync(principal.UserId(), ct) ?? throw new ApiException(401, "Unauthorized: Session has expired or is invalid");
            return Results.Ok(new { valid = true, user = UserService.ToDto(user) });
        }).RequireAuthorization();

        // Where the account is signed in: the user can end a lost device's session without changing the password
        auth.MapGet("/sessions", async (ClaimsPrincipal user, SessionService sessions, CancellationToken ct) =>
            Results.Ok(await sessions.ListAsync(user.UserId(), user.SessionIdOf(), ct))).RequireAuthorization();

        auth.MapDelete("/sessions/{id}", async (string id, HttpContext context, ClaimsPrincipal user, SessionService sessions, ILoggerFactory loggers, CancellationToken ct) =>
        {
            await sessions.EndAsync(user.UserId(), id, ct);
            if (id == Ids.Format(user.SessionIdOf())) SessionCookies.Clear(context);
            SecurityLog.SessionEnded(loggers.CreateLogger(SecurityLog.Category), Ids.Format(user.UserId()), id, SecurityLog.Client(context));
            return Results.NoContent();
        }).RequireAuthorization();

        auth.MapDelete("/sessions", async (HttpContext context, ClaimsPrincipal user, SessionService sessions, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var ended = await sessions.EndOthersAsync(user.UserId(), user.SessionIdOf(), ct);
            SecurityLog.OtherSessionsEnded(loggers.CreateLogger(SecurityLog.Category), Ids.Format(user.UserId()), ended, SecurityLog.Client(context));
            return Results.Ok(new { ended });
        }).RequireAuthorization();

        auth.MapPost("/password", async (ChangePasswordRequest body, ClaimsPrincipal user, UserService users, ILoggerFactory loggers, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(body.CurrentPassword) || body.NewPassword is null) throw ApiException.BadRequest("Current and new password are required");
            await users.ChangeOwnPasswordAsync(user.UserId(), user.SessionIdOf(), body.CurrentPassword, body.NewPassword, ct);
            SecurityLog.PasswordChanged(loggers.CreateLogger(SecurityLog.Category), Ids.Format(user.UserId()));
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting(RateLimits.LoginPolicy);
    }

    private static void MapNotebooks(RouteGroupBuilder group)
    {
        group.MapGet("", async (ClaimsPrincipal user, NotebookService notebooks, CancellationToken ct) =>
            Results.Ok(await notebooks.GetTreeAsync(user.UserId(), ct)));

        group.MapGet("/trash", async (ClaimsPrincipal user, NotebookService notebooks, CancellationToken ct) =>
            Results.Ok(await notebooks.GetTrashAsync(user.UserId(), ct)));

        // Kept for the client's "Refresh" (changes made on other devices): there is no cache to reload, so it only answers the tree
        group.MapPost("/refresh", async (ClaimsPrincipal user, NotebookService notebooks, CancellationToken ct) =>
            Results.Ok(await notebooks.GetTreeAsync(user.UserId(), ct)));

        group.MapPost("", async (NotebookRequest body, ClaimsPrincipal user, NotebookService notebooks, CancellationToken ct) =>
            Results.Created((string?)null, await notebooks.CreateAsync(user.UserId(), body.Title ?? "", body.ParentId, ct)));

        group.MapPut("/{id}", async (string id, NotebookRequest body, ClaimsPrincipal user, NotebookService notebooks, CancellationToken ct) =>
            Results.Ok(await notebooks.UpdateAsync(user.UserId(), id, body.Title, body.ParentId, ct)));

        group.MapDelete("/{id}", async (string id, string? permanent, ClaimsPrincipal user, NotebookService notebooks, CancellationToken ct) =>
            Results.Ok(IsTrue(permanent)
                ? await notebooks.PurgeAsync(user.UserId(), id, ct)
                : await notebooks.TrashAsync(user.UserId(), id, ct)));

        group.MapPost("/{id}/restore", async (string id, ClaimsPrincipal user, NotebookService notebooks, CancellationToken ct) =>
            Results.Ok(await notebooks.RestoreAsync(user.UserId(), id, ct)));
    }

    private static void MapNotes(RouteGroupBuilder group)
    {
        group.MapGet("", async (HttpRequest request, ClaimsPrincipal user, NoteService notes, CancellationToken ct) =>
        {
            var q = request.Query;
            var notebookId = q["notebook_id"].FirstOrDefault() ?? q["notebookId"].FirstOrDefault();
            var tagId = q["tag_id"].FirstOrDefault() ?? q["tagId"].FirstOrDefault();
            var trash = IsTrue(q["is_trash"].FirstOrDefault() ?? q["isTrash"].FirstOrDefault());
            return Results.Ok(await notes.ListAsync(user.UserId(), notebookId, tagId, trash, ct));
        });

        group.MapGet("/{id}", async (string id, ClaimsPrincipal user, NoteService notes, CancellationToken ct) =>
            Results.Ok(await notes.GetAsync(user.UserId(), id, ct)));

        group.MapPost("", async (CreateNoteRequest body, ClaimsPrincipal user, NoteService notes, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(body.ParentId)) throw ApiException.BadRequest("parentId (notebook id) is required");
            return Results.Created((string?)null, await notes.CreateAsync(user.UserId(), body.Title, body.Body, body.ParentId, body.IsTodo ?? false, ct));
        });

        group.MapPut("/{id}", async (string id, UpdateNoteRequest body, ClaimsPrincipal user, NoteService notes, CancellationToken ct) =>
        {
            if (body.TodoDue is < 0 || body.TodoDue > MaxDateMs) throw ApiException.BadRequest("todoDue must be a timestamp in milliseconds, 0 or null");
            var changes = new NoteUpdate(body.Title, body.Body, body.ParentId, body.TodoCompleted, body.TodoDue, body.BaseUpdatedTime, body.Force ?? false);
            return Results.Ok(await notes.UpdateAsync(user.UserId(), id, changes, ct));
        });

        group.MapDelete("/{id}", async (string id, string? permanent, ClaimsPrincipal user, NoteService notes, CancellationToken ct) =>
        {
            if (IsTrue(permanent)) await notes.PurgeAsync(user.UserId(), id, ct);
            else await notes.TrashAsync(user.UserId(), id, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id}/restore", async (string id, ClaimsPrincipal user, NoteService notes, CancellationToken ct) =>
            Results.Ok(await notes.RestoreAsync(user.UserId(), id, ct)));

        group.MapPost("/{id}/tags", async (string id, TagRequest body, ClaimsPrincipal user, NoteService notes, CancellationToken ct) =>
            Results.Ok(await notes.AddTagAsync(user.UserId(), id, body.Title ?? "", ct)));

        group.MapDelete("/{id}/tags/{tagId}", async (string id, string tagId, ClaimsPrincipal user, NoteService notes, CancellationToken ct) =>
            Results.Ok(await notes.RemoveTagAsync(user.UserId(), id, tagId, ct)));

        group.MapGet("/{id}/versions", async (string id, ClaimsPrincipal user, NoteService notes, VersionService versions, CancellationToken ct) =>
        {
            var note = await notes.FindAsync(user.UserId(), id, ct);
            return Results.Ok(await versions.ListAsync(user.UserId(), note.Id, ct));
        });

        group.MapGet("/{id}/versions/{versionId}", async (string id, string versionId, ClaimsPrincipal user, NoteService notes, VersionService versions, CancellationToken ct) =>
        {
            var note = await notes.FindAsync(user.UserId(), id, ct);
            return Results.Ok(VersionService.ToDetail(await versions.FindAsync(user.UserId(), note.Id, versionId, ct)));
        });

        group.MapPost("/{id}/versions/{versionId}/restore", async (string id, string versionId, RestoreVersionRequest? body, ClaimsPrincipal user, NoteService notes, CancellationToken ct) =>
            Results.Ok(await notes.RestoreVersionAsync(user.UserId(), id, versionId, body?.BaseUpdatedTime, ct)));
    }

    private static void MapTags(RouteGroupBuilder group)
    {
        group.MapGet("", async (ClaimsPrincipal user, TagService tags, CancellationToken ct) =>
            Results.Ok(await tags.ListAsync(user.UserId(), ct)));

        group.MapPut("/{id}", async (string id, TagRequest body, ClaimsPrincipal user, TagService tags, CancellationToken ct) =>
            Results.Ok(await tags.RenameAsync(user.UserId(), id, body.Title ?? "", ct)));

        group.MapDelete("/{id}", async (string id, ClaimsPrincipal user, TagService tags, CancellationToken ct) =>
            Results.Ok(new { notes = await tags.DeleteAsync(user.UserId(), id, ct) }));
    }

    private static void MapResources(RouteGroupBuilder group)
    {
        // The body is the raw file; the name comes URI-encoded in X-File-Name, the type in X-File-Type
        group.MapPost("", async (HttpRequest request, ClaimsPrincipal user, ResourceService resources, CancellationToken ct) =>
        {
            var rawName = request.Headers["X-File-Name"].ToString();
            string name;
            try
            {
                name = Uri.UnescapeDataString(rawName);
            }
            catch (UriFormatException)
            {
                name = rawName;
            }
            var created = await resources.CreateAsync(user.UserId(), name, request.Headers["X-File-Type"].ToString(), request.Body, ct);
            return Results.Created((string?)null, created);
        });

        group.MapGet("/{id}", async (string id, HttpContext context, ClaimsPrincipal user, ResourceService resources, CancellationToken ct) =>
        {
            var (resource, content) = await resources.OpenAsync(user.UserId(), id, ct);
            var (contentType, inline) = ResourceTypes.ResponseType(resource.MimeType);
            var headers = context.Response.Headers;
            headers.CacheControl = "private, max-age=86400, immutable";
            headers.XContentTypeOptions = "nosniff";
            // Attachments are user content served from our origin: never let them run scripts
            headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; sandbox";
            headers.ContentDisposition = ResourceTypes.ContentDisposition(inline, resource.FileName);
            return Results.Stream(content, contentType);
        });

        group.MapDelete("/{id}", async (string id, ClaimsPrincipal user, ResourceService resources, CancellationToken ct) =>
        {
            await resources.DeleteAsync(user.UserId(), id, ct);
            return Results.NoContent();
        });
    }

    /// <summary>Who made an admin change, for the security log.</summary>
    private static string Actor(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Email) ?? Ids.Format(user.UserId());

    private static void MapAdmin(RouteGroupBuilder group)
    {
        group.MapGet("/users", async (UserService users, CancellationToken ct) => Results.Ok(await users.ListAsync(ct)));

        group.MapPost("/users", async (CreateUserRequest body, ClaimsPrincipal user, UserService users, CancellationToken ct) =>
            Results.Created((string?)null, await users.CreateAsync(Actor(user), body.Email ?? "", body.Password ?? "", body.DisplayName, body.IsAdmin ?? false, ct)));

        group.MapPut("/users/{id}", async (string id, UpdateUserRequest body, ClaimsPrincipal user, UserService users, CancellationToken ct) =>
            Results.Ok(await users.UpdateAsync(Actor(user), Ids.Parse(id, "user id"), body.Password, body.DisplayName, body.IsAdmin,
                body.QuotaMb, body.ResetQuota ?? false, ct)));

        group.MapDelete("/users/{id}", async (string id, ClaimsPrincipal user, UserService users, CancellationToken ct) =>
        {
            var userId = Ids.Parse(id, "user id");
            if (userId == user.UserId()) throw ApiException.Conflict("You can't delete your own account");
            await users.DeleteAsync(Actor(user), userId, ct);
            return Results.NoContent();
        });
    }

    private static bool IsTrue(string? value) => value is "true" or "1";
}
