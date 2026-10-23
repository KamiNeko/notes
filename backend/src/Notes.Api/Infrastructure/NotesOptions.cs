namespace Notes.Api.Infrastructure;

/// <summary>
/// Application settings, bound from the <c>Notes</c> configuration section
/// (environment variables <c>Notes__DataDirectory</c>, <c>Notes__PublicUrl</c>, ...).
/// </summary>
public class NotesOptions
{
    public const string Section = "Notes";

    /// <summary>Where attachments are stored. Relative paths are resolved against the working directory.</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>The URL users open, e.g. <c>https://notes.example.com</c>. Used for the CSRF origin check and the cookie's Secure flag.</summary>
    public string? PublicUrl { get; set; }

    /// <summary>Honour <c>X-Forwarded-*</c> headers from a reverse proxy.</summary>
    public bool TrustProxy { get; set; }

    /// <summary><c>auto</c> (Secure over HTTPS), <c>true</c> or <c>false</c>.</summary>
    public string CookieSecure { get; set; } = "auto";

    /// <summary>A session ends after this many days without use.</summary>
    public int SessionLifetimeDays { get; set; } = 30;

    /// <summary>Edits within this many minutes of the last version don't create another one.</summary>
    public int VersionIntervalMinutes { get; set; } = 10;

    /// <summary>Versions older than this are deleted; 0 keeps them forever.</summary>
    public int VersionRetentionDays { get; set; }

    /// <summary>Largest attachment accepted, in MB.</summary>
    public int MaxUploadMb { get; set; } = 25;

    /// <summary>API requests per client address and 15 minutes.</summary>
    public int RateLimitMax { get; set; } = 1000;

    /// <summary>Login attempts per client address and 15 minutes.</summary>
    public int LoginRateLimitMax { get; set; } = 30;

    /// <summary>Allow <c>https:</c> images in notes (otherwise only attachments and data URLs load).</summary>
    public bool AllowRemoteImages { get; set; }

    /// <summary>Creates the demo account with sample notes on startup (development and screenshots only).</summary>
    public bool SeedDemo { get; set; }

    /// <summary>Created as administrator on startup when there is no user yet.</summary>
    public AdminOptions Admin { get; set; } = new();
}

/// <summary>The first administrator account.</summary>
public class AdminOptions
{
    public string? Email { get; set; }

    public string? Password { get; set; }
}
