namespace Notes.Api.Data;

/// <summary>An account. Users are created by an administrator; there is no public registration.</summary>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Lower-cased, unique; used to sign in.</summary>
    public string Email { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public bool IsAdmin { get; set; }

    /// <summary>Attachment space in MB set by an administrator (0: unlimited); null uses <c>Notes__QuotaMb</c>.</summary>
    public int? QuotaMb { get; set; }
    /// <summary>TOTP secret (RFC 6238) while two-factor sign-in is on; null when it is off.</summary>
    public byte[]? TotpSecret { get; set; }

    /// <summary>Secret shown during setup, until it is confirmed with a code.</summary>
    public byte[]? TotpPendingSecret { get; set; }

    /// <summary>Time step of the last accepted code; a code is never accepted twice.</summary>
    public long TotpLastStep { get; set; }

    public DateTimeOffset CreatedTime { get; set; }

    public DateTimeOffset UpdatedTime { get; set; }
}

/// <summary>A one-time recovery code for two-factor sign-in. Only the SHA-256 hash is stored; it is deleted when used.</summary>
public class RecoveryCode
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public byte[] CodeHash { get; set; } = [];

    public DateTimeOffset CreatedTime { get; set; }
}

/// <summary>A signed-in browser. Only the SHA-256 hash of the cookie token is stored.</summary>
public class Session
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public DateTimeOffset CreatedTime { get; set; }

    public DateTimeOffset LastSeenTime { get; set; }

    public DateTimeOffset ExpiresTime { get; set; }

    /// <summary>The browser's User-Agent at sign-in, shortened; shown in the user's session list. No address is stored.</summary>
    public string UserAgent { get; set; } = "";
}

/// <summary>A notebook; notebooks nest through <see cref="ParentId"/>.</summary>
public class Notebook
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Parent notebook, null at the top level.</summary>
    public Guid? ParentId { get; set; }

    public string Title { get; set; } = "";

    public DateTimeOffset CreatedTime { get; set; }

    public DateTimeOffset UpdatedTime { get; set; }

    /// <summary>When the notebook was moved to the trash; null while it is active.</summary>
    public DateTimeOffset? DeletedTime { get; set; }
}

/// <summary>A note or to-do. The body is Markdown (or HTML when <see cref="MarkupLanguage"/> is 2).</summary>
public class Note
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid NotebookId { get; set; }

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    /// <summary>Plain-text preview of the body for lists, kept up to date on every write.</summary>
    public string Preview { get; set; } = "";

    /// <summary>1 = Markdown, 2 = HTML (e.g. pages saved with a web clipper).</summary>
    public int MarkupLanguage { get; set; } = 1;

    public bool IsTodo { get; set; }

    /// <summary>When the to-do was completed; null while open.</summary>
    public DateTimeOffset? TodoCompletedTime { get; set; }

    /// <summary>To-do due date; null when none is set.</summary>
    public DateTimeOffset? TodoDueTime { get; set; }

    public DateTimeOffset CreatedTime { get; set; }

    public DateTimeOffset UpdatedTime { get; set; }

    public DateTimeOffset? DeletedTime { get; set; }

    public List<NoteTag> NoteTags { get; set; } = [];
}

/// <summary>A tag; titles are unique per user, case-insensitively.</summary>
public class Tag
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Title { get; set; } = "";

    public DateTimeOffset CreatedTime { get; set; }

    public DateTimeOffset UpdatedTime { get; set; }

    public List<NoteTag> NoteTags { get; set; } = [];
}

/// <summary>Links a note to a tag.</summary>
public class NoteTag
{
    public Guid NoteId { get; set; }

    public Note? Note { get; set; }

    public Guid TagId { get; set; }

    public Tag? Tag { get; set; }

    public DateTimeOffset CreatedTime { get; set; }
}

/// <summary>Metadata of an attachment; the file itself lives in the attachment store.</summary>
public class Resource
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string FileName { get; set; } = "";

    public string MimeType { get; set; } = "application/octet-stream";

    public long Size { get; set; }

    public DateTimeOffset CreatedTime { get; set; }
}

/// <summary>An earlier state of a note's title and body (version history).</summary>
public class NoteVersion
{
    public Guid Id { get; set; }

    public Guid NoteId { get; set; }

    public Guid UserId { get; set; }

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    /// <summary>The note's <see cref="Note.UpdatedTime"/> when it had this content.</summary>
    public DateTimeOffset NoteUpdatedTime { get; set; }

    /// <summary>When the snapshot was taken.</summary>
    public DateTimeOffset CreatedTime { get; set; }
}
