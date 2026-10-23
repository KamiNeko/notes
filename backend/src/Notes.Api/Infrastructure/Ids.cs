using System.Diagnostics.CodeAnalysis;

namespace Notes.Api.Infrastructure;

/// <summary>
/// Ids are sent as 32 lower-case hex characters (<c>Guid.ToString("N")</c>), the same shape Joplin uses,
/// so notes copied over keep working <c>:/id</c> links.
/// </summary>
public static class Ids
{
    /// <summary>Formats an id for the API.</summary>
    public static string Format(Guid id) => id.ToString("N");

    /// <summary>Formats an optional id; null becomes an empty string.</summary>
    public static string Format(Guid? id) => id is { } value ? Format(value) : "";

    /// <summary>Parses a 32-hex id, or returns false.</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out Guid? id)
    {
        id = null;
        if (value is null || value.Length != 32 || !Guid.TryParseExact(value, "N", out var parsed)) return false;
        id = parsed;
        return true;
    }

    /// <summary>Parses a 32-hex id or throws a 400 naming <paramref name="what"/>.</summary>
    public static Guid Parse(string? value, string what) =>
        TryParse(value, out var id) ? id.Value : throw ApiException.BadRequest($"Invalid {what}");
}
