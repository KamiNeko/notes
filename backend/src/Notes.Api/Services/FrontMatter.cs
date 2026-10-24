using System.Globalization;
using System.Text;

namespace Notes.Api.Services;

/// <summary>
/// The YAML front matter keys Notes reads on import, as the export writes them: <c>title</c>, <c>created</c>, <c>updated</c>, <c>tags</c>,
/// <c>completed?</c> (marks a to-do) and <c>due</c>. Only this flat subset of YAML is understood.
/// </summary>
public record FrontMatter(string? Title, DateTimeOffset? Created, DateTimeOffset? Updated, List<string> Tags, bool? Completed, DateTimeOffset? Due)
{
    private static readonly FrontMatter Empty = new(null, null, null, [], null, null);

    /// <summary>Splits a note file into its front matter and body; a file without front matter is all body.</summary>
    public static (FrontMatter Front, string Body) Parse(string text)
    {
        text = text.TrimStart('﻿').Replace("\r\n", "\n");
        if (!text.StartsWith("---\n", StringComparison.Ordinal)) return (Empty, text);
        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return (Empty, text);
        var afterEnd = text.IndexOf('\n', end + 4);
        var closing = text[(end + 1)..(afterEnd < 0 ? text.Length : afterEnd)];
        if (closing.TrimEnd() != "---") return (Empty, text);
        var body = afterEnd < 0 ? "" : text[(afterEnd + 1)..];
        // The export separates front matter and body with one blank line
        if (body.StartsWith('\n')) body = body[1..];

        string? title = null;
        DateTimeOffset? created = null, updated = null, due = null;
        bool? completed = null;
        var tags = new List<string>();
        string? listKey = null;
        foreach (var line in (end > 4 ? text[4..end] : "").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) && listKey == "tags")
            {
                tags.Add(Unquote(trimmed[2..]));
                continue;
            }
            var colon = line.IndexOf(':');
            // "completed?:" has its colon after the question mark
            if (colon < 0) continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            listKey = value.Length == 0 ? key : null;
            switch (key)
            {
                case "title":
                    title = Unquote(value);
                    break;
                case "created":
                    created = Date(value);
                    break;
                case "updated":
                    updated = Date(value);
                    break;
                case "due":
                    due = Date(value);
                    break;
                case "completed?":
                    completed = Unquote(value).ToLowerInvariant() switch { "yes" or "true" => true, "no" or "false" => false, _ => null };
                    break;
                case "tags" when value.StartsWith('[') && value.EndsWith(']'):
                    tags.AddRange(value[1..^1].Split(',').Select(Unquote).Where(t => t.Length > 0));
                    break;
                case "tags" when value.Length > 0:
                    tags.Add(Unquote(value));
                    break;
            }
        }
        return (new FrontMatter(title, created, updated, tags, completed, due), body);
    }

    private static DateTimeOffset? Date(string value) =>
        DateTimeOffset.TryParse(Unquote(value), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    /// <summary>A YAML scalar without its quotes (double-quoted with <c>\"</c> and <c>\\</c> escapes, or single-quoted).</summary>
    internal static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'') return value[1..^1].Replace("''", "'");
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"') return value;
        var result = new StringBuilder();
        for (var i = 1; i < value.Length - 1; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length - 1)
            {
                i++;
                result.Append(value[i] switch { 'n' => '\n', 't' => '\t', var c => c });
            }
            else
            {
                result.Append(value[i]);
            }
        }
        return result.ToString();
    }
}
