using System.Text.RegularExpressions;
using Notes.Api.Infrastructure;

namespace Notes.Api.Services;

/// <summary>Normalizes titles of notes, notebooks and tags.</summary>
public static partial class Titles
{
    public const int MaxLength = 500;

    /// <summary>Puts a title on one line (line breaks become spaces), trims it and caps its length.</summary>
    public static string Clean(string? title)
    {
        var text = LineBreaks().Replace(title ?? "", " ").Trim();
        return text.Length > MaxLength ? text[..MaxLength].TrimEnd() : text;
    }

    /// <summary>Like <see cref="Clean"/>, but an empty result is a 400 naming <paramref name="what"/>.</summary>
    public static string Require(string? title, string what)
    {
        var clean = Clean(title);
        return clean.Length > 0 ? clean : throw ApiException.BadRequest($"{what} is required");
    }

    [GeneratedRegex(@"[\r\n]+")]
    private static partial Regex LineBreaks();
}
