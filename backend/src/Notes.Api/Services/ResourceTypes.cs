using System.Text;
using System.Text.RegularExpressions;

namespace Notes.Api.Services;

/// <summary>MIME type detection and the safe way to serve attachments (user content from our origin).</summary>
public static partial class ResourceTypes
{
    private static readonly Dictionary<string, string> MimeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = "image/png", ["jpg"] = "image/jpeg", ["jpeg"] = "image/jpeg", ["gif"] = "image/gif",
        ["webp"] = "image/webp", ["svg"] = "image/svg+xml", ["bmp"] = "image/bmp", ["avif"] = "image/avif",
        ["pdf"] = "application/pdf", ["txt"] = "text/plain", ["md"] = "text/markdown", ["csv"] = "text/csv",
        ["json"] = "application/json", ["zip"] = "application/zip", ["mp3"] = "audio/mpeg", ["mp4"] = "video/mp4",
        ["webm"] = "video/webm", ["ogg"] = "audio/ogg", ["wav"] = "audio/wav",
    };

    /// <summary>Shown inline: these can't run script in our origin.</summary>
    private static readonly HashSet<string> InlineSafeTypes =
    [
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp", "image/avif", "image/x-icon",
        "image/vnd.microsoft.icon", "application/pdf", "text/plain",
    ];

    /// <summary>File name for storage and downloads: no path separators or control characters, at most 255 characters.</summary>
    public static string CleanFileName(string? name)
    {
        var source = name ?? "";
        var builder = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (char.IsHighSurrogate(c) && i + 1 < source.Length && char.IsLowSurrogate(source[i + 1]))
            {
                builder.Append(c).Append(source[++i]);
            }
            else if (!char.IsControl(c) && !char.IsSurrogate(c))
            {
                builder.Append(c is '/' or '\\' ? '_' : c);
            }
        }
        var clean = builder.ToString().Trim();
        // Never cut a surrogate pair in half
        if (clean.Length > 255) clean = clean[..(char.IsHighSurrogate(clean[254]) ? 254 : 255)];
        return clean.Length > 0 ? clean : "file";
    }

    /// <summary>
    /// The type to store: the browser's when it is specific, else the one for the extension, else
    /// one detected from the first bytes, else <c>application/octet-stream</c>.
    /// </summary>
    public static string ChooseMimeType(string? declared, string fileName, ReadOnlySpan<byte> head)
    {
        var type = (declared ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (SimpleMime().IsMatch(type) && type != "application/octet-stream") return type;
        var dot = fileName.LastIndexOf('.');
        if (dot >= 0 && MimeByExtension.TryGetValue(fileName[(dot + 1)..], out var byExtension)) return byExtension;
        return Detect(head) ?? "application/octet-stream";
    }

    /// <summary>Detects common types from magic numbers.</summary>
    public static string? Detect(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4e && b[3] == 0x47) return "image/png";
        if (b.Length >= 4 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38) return "image/gif";
        if (b.Length >= 4 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46) return "application/pdf";
        if (b.Length >= 3 && b[0] == 0xff && b[1] == 0xd8 && b[2] == 0xff) return "image/jpeg";
        if (b.Length >= 12 && Encoding.ASCII.GetString(b[..4]) == "RIFF" && Encoding.ASCII.GetString(b[8..12]) == "WEBP") return "image/webp";
        var text = Encoding.UTF8.GetString(b[..Math.Min(b.Length, 256)]).Trim().ToLowerInvariant();
        if (text.StartsWith("<svg", StringComparison.Ordinal) || (text.StartsWith("<?xml", StringComparison.Ordinal) && text.Contains("<svg"))) return "image/svg+xml";
        return null;
    }

    /// <summary>
    /// How an attachment is served: raster images, PDF, plain text, audio and video inline; SVG keeps its
    /// type (so <c>&lt;img&gt;</c> shows it) but is a download; everything else becomes
    /// <c>application/octet-stream</c> and a download.
    /// </summary>
    public static (string ContentType, bool Inline) ResponseType(string? mime)
    {
        var type = (mime ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (InlineSafeTypes.Contains(type)) return (type, true);
        if (MimePattern().IsMatch(type) && (type.StartsWith("audio/", StringComparison.Ordinal) || type.StartsWith("video/", StringComparison.Ordinal))) return (type, true);
        if (type == "image/svg+xml") return (type, false);
        return ("application/octet-stream", false);
    }

    /// <summary>A Content-Disposition value with an ASCII fallback and an RFC 5987 UTF-8 file name.</summary>
    public static string ContentDisposition(bool inline, string? fileName)
    {
        var kind = inline ? "inline" : "attachment";
        var name = CleanFileName(fileName);
        var ascii = AsciiUnsafe().Replace(name, "_");
        var encoded = Uri.EscapeDataString(name).Replace("'", "%27").Replace("(", "%28").Replace(")", "%29").Replace("*", "%2A");
        return $"{kind}; filename=\"{ascii}\"; filename*=UTF-8''{encoded}";
    }

    [GeneratedRegex(@"^[a-z0-9.+-]+/[a-z0-9.+-]+$")]
    private static partial Regex SimpleMime();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9!#$&^_.+-]*/[a-z0-9][a-z0-9!#$&^_.+-]*$")]
    private static partial Regex MimePattern();

    [GeneratedRegex(@"[^\x20-\x7e]|[""\\]")]
    private static partial Regex AsciiUnsafe();
}
