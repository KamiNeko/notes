namespace Notes.Api.Infrastructure;

/// <summary>
/// Security events (sign-ins, account changes, refused requests), logged under the category
/// <c>Notes.Security</c> with stable event ids so they can be filtered or forwarded. Never logs
/// passwords or session tokens.
/// </summary>
public static partial class SecurityLog
{
    /// <summary>The logger category of all security events.</summary>
    public const string Category = "Notes.Security";

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Sign-in succeeded for {Email} from {Client}")]
    public static partial void LoginSucceeded(ILogger logger, string email, string client);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Sign-in failed for {Email} from {Client}")]
    public static partial void LoginFailed(ILogger logger, string email, string client);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Sign-in refused for {Email} from {Client}: too many failed attempts, the account is locked for now")]
    public static partial void LoginLocked(ILogger logger, string email, string client);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Signed out {UserId}")]
    public static partial void LoggedOut(ILogger logger, string userId);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Information, Message = "{UserId} changed their password; their other sessions were ended")]
    public static partial void PasswordChanged(ILogger logger, string userId);

    [LoggerMessage(EventId = 1020, Level = LogLevel.Information, Message = "{Actor} created user {Email} (administrator: {IsAdmin})")]
    public static partial void UserCreated(ILogger logger, string actor, string email, bool isAdmin);

    [LoggerMessage(EventId = 1021, Level = LogLevel.Information, Message = "{Actor} set a new password for {Email}; all their sessions were ended")]
    public static partial void PasswordReset(ILogger logger, string actor, string email);

    [LoggerMessage(EventId = 1022, Level = LogLevel.Information, Message = "{Actor} changed the administrator role of {Email} to {IsAdmin}")]
    public static partial void RoleChanged(ILogger logger, string actor, string email, bool isAdmin);

    [LoggerMessage(EventId = 1023, Level = LogLevel.Information, Message = "{Actor} deleted user {Email} and all their data")]
    public static partial void UserDeleted(ILogger logger, string actor, string email);

    [LoggerMessage(EventId = 1024, Level = LogLevel.Information, Message = "{Actor} set the storage limit of {Email} to {Quota}")]
    public static partial void QuotaChanged(ILogger logger, string actor, string email, string quota);

    [LoggerMessage(EventId = 1030, Level = LogLevel.Warning, Message = "Refused {Method} {Path} from {Client}: origin {Origin} is not allowed")]
    public static partial void OriginRejected(ILogger logger, string method, string path, string client, string origin);

    [LoggerMessage(EventId = 1031, Level = LogLevel.Warning, Message = "Rate limit hit by {Client} on {Method} {Path}")]
    public static partial void RateLimited(ILogger logger, string client, string method, string path);

    [LoggerMessage(EventId = 1040, Level = LogLevel.Warning, Message = "The demo account {Email} with its published password exists. Never enable Notes__SeedDemo on a server others can reach.")]
    public static partial void DemoAccountPresent(ILogger logger, string email);

    [LoggerMessage(EventId = 1050, Level = LogLevel.Information, Message = "{UserId} exported all their notes from {Client}")]
    public static partial void DataExported(ILogger logger, string userId, string client);

    /// <summary>The client address for log lines (after forwarded headers, when trusted).</summary>
    public static string Client(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
