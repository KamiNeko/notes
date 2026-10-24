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

    /// <summary>Honour <c>X-Forwarded-*</c> headers from a reverse proxy (only from <see cref="TrustedProxies"/>).</summary>
    public bool TrustProxy { get; set; }

    /// <summary>
    /// Comma-separated addresses or CIDR ranges the reverse proxy connects from. Empty: loopback and the
    /// private ranges (10/8, 172.16/12, 192.168/16, fc00::/7), which covers a proxy on the LAN or in Docker.
    /// </summary>
    public string? TrustedProxies { get; set; }

    /// <summary><c>auto</c> (Secure over HTTPS), <c>true</c> or <c>false</c>.</summary>
    public string CookieSecure { get; set; } = "auto";

    /// <summary>A session ends after this many days without use.</summary>
    public int SessionLifetimeDays { get; set; } = 30;

    /// <summary>A session ends this many days after sign-in, however often it is used.</summary>
    public int SessionMaxAgeDays { get; set; } = 90;

    /// <summary>Edits within this many minutes of the last version don't create another one.</summary>
    public int VersionIntervalMinutes { get; set; } = 10;

    /// <summary>Versions older than this are deleted; 0 keeps them forever.</summary>
    public int VersionRetentionDays { get; set; }

    /// <summary>Notes and notebooks in the trash longer than this are deleted for good; 0 keeps them until the user empties the trash.</summary>
    public int TrashRetentionDays { get; set; }

    /// <summary>Largest attachment accepted, in MB.</summary>
    public int MaxUploadMb { get; set; } = 25;

    /// <summary>API requests per client address and 15 minutes.</summary>
    public int RateLimitMax { get; set; } = 1000;

    /// <summary>Login attempts per client address and 15 minutes.</summary>
    public int LoginRateLimitMax { get; set; } = 30;

    /// <summary>Failed sign-ins for one account (from any address) before it is locked for 15 minutes.</summary>
    public int LoginMaxFailures { get; set; } = 10;

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

/// <summary>
/// Checks <see cref="NotesOptions"/> on startup. A typo in a setting stops the app with a message
/// naming the variable, instead of running with a different (often less secure) policy than intended.
/// </summary>
public static class NotesOptionsValidator
{
    /// <summary>What is wrong with the settings; empty when they are valid.</summary>
    public static List<string> Validate(NotesOptions options)
    {
        var errors = new List<string>();
        if (!string.IsNullOrEmpty(options.CookieSecure) && options.CookieSecure.ToLowerInvariant() is not ("auto" or "true" or "false"))
        {
            errors.Add($"Notes__CookieSecure must be auto, true or false, not '{options.CookieSecure}'");
        }
        if (!string.IsNullOrWhiteSpace(options.PublicUrl)
            && !(Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var url) && url.Scheme is "http" or "https"))
        {
            errors.Add($"Notes__PublicUrl must be an absolute http(s) URL such as https://notes.example.com, not '{options.PublicUrl}'");
        }
        AtLeast(errors, nameof(options.SessionLifetimeDays), options.SessionLifetimeDays, 1);
        AtLeast(errors, nameof(options.SessionMaxAgeDays), options.SessionMaxAgeDays, 1);
        AtLeast(errors, nameof(options.VersionIntervalMinutes), options.VersionIntervalMinutes, 0);
        AtLeast(errors, nameof(options.VersionRetentionDays), options.VersionRetentionDays, 0);
        AtLeast(errors, nameof(options.TrashRetentionDays), options.TrashRetentionDays, 0);
        AtLeast(errors, nameof(options.MaxUploadMb), options.MaxUploadMb, 1);
        AtLeast(errors, nameof(options.RateLimitMax), options.RateLimitMax, 1);
        AtLeast(errors, nameof(options.LoginRateLimitMax), options.LoginRateLimitMax, 1);
        AtLeast(errors, nameof(options.LoginMaxFailures), options.LoginMaxFailures, 1);
        if (options.TrustProxy)
        {
            try
            {
                TrustedProxies.Parse(options.TrustedProxies);
            }
            catch (InvalidOperationException error)
            {
                errors.Add(error.Message);
            }
        }
        return errors;
    }

    /// <summary>Throws when the settings are invalid, listing every problem.</summary>
    public static void ThrowIfInvalid(NotesOptions options)
    {
        var errors = Validate(options);
        if (errors.Count > 0) throw new InvalidOperationException("Invalid settings: " + string.Join("; ", errors));
    }

    private static void AtLeast(List<string> errors, string name, int value, int minimum)
    {
        if (value < minimum) errors.Add($"Notes__{name} must be at least {minimum}, not {value}");
    }
}
