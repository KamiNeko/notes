using System.Text.RegularExpressions;

namespace Notes.Api.Services;

/// <summary>
/// A parsed search query (Joplin desktop's syntax, as in Joplin Web):
/// <list type="bullet">
/// <item><c>shopping list</c>: every word must appear in the title or body (any order, case-insensitive)</item>
/// <item><c>"shopping list"</c>: an exact phrase; an unclosed quote runs to the end</item>
/// <item><c>-word</c>, <c>-"some phrase"</c>: the note must not contain it</item>
/// <item><c>tag:name</c>: the note has this tag (several must all match); <c>-tag:</c> excludes</item>
/// <item><c>notebook:name</c>: the note is in this notebook or a sub-notebook (several match any)</item>
/// <item><c>type:todo</c> / <c>type:note</c>, <c>iscompleted:1</c> / <c>iscompleted:0</c></item>
/// </list>
/// <c>*</c> in a tag or notebook name is a wildcard. Unknown filters and invalid values are searched as words.
/// All text values are lower-cased.
/// </summary>
public sealed partial class SearchQuery
{
    public List<string> Terms { get; } = [];

    public List<string> ExcludedTerms { get; } = [];

    public List<string> Tags { get; } = [];

    public List<string> ExcludedTags { get; } = [];

    public List<string> Notebooks { get; } = [];

    public List<string> ExcludedNotebooks { get; } = [];

    /// <summary><c>todo</c>, <c>note</c> or null.</summary>
    public string? Type { get; private set; }

    /// <summary>Set by <c>iscompleted:</c>; implies to-dos.</summary>
    public bool? Completed { get; private set; }

    /// <summary>True when there is nothing to search for (it would match everything).</summary>
    public bool IsEmpty =>
        Terms.Count == 0 && ExcludedTerms.Count == 0 && Tags.Count == 0 && ExcludedTags.Count == 0 &&
        Notebooks.Count == 0 && ExcludedNotebooks.Count == 0 && Type is null && Completed is null;

    private static readonly HashSet<string> Filters = ["tag", "notebook", "type", "iscompleted"];

    private sealed record Token(string Text, bool Negated, string? Filter, bool Quoted);

    /// <summary>Parses a query as typed by the user.</summary>
    public static SearchQuery Parse(string query)
    {
        var parsed = new SearchQuery();
        foreach (var token in Tokenize(query))
        {
            var value = token.Text.ToLowerInvariant();
            var terms = token.Negated ? parsed.ExcludedTerms : parsed.Terms;
            switch (token.Filter)
            {
                case "tag":
                    (token.Negated ? parsed.ExcludedTags : parsed.Tags).Add(value);
                    break;
                case "notebook":
                    (token.Negated ? parsed.ExcludedNotebooks : parsed.Notebooks).Add(value);
                    break;
                case "type":
                    if (!token.Negated && value is "todo" or "note") parsed.Type = value;
                    else terms.Add($"type:{value}");
                    break;
                case "iscompleted":
                    if (!token.Negated && value is "1" or "0") parsed.Completed = value == "1";
                    else terms.Add($"iscompleted:{value}");
                    break;
                default:
                    terms.Add(value);
                    break;
            }
        }
        return parsed;
    }

    private static List<Token> Tokenize(string query)
    {
        var tokens = new List<Token>();
        var i = 0;
        var n = query.Length;
        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(query[i])) i++;
            if (i >= n) break;

            var negated = false;
            if (query[i] == '-' && i + 1 < n && !char.IsWhiteSpace(query[i + 1]))
            {
                negated = true;
                i++;
            }

            string? filter = null;
            var filterMatch = FilterPrefix().Match(query, i);
            if (filterMatch.Success && Filters.Contains(filterMatch.Groups[1].Value.ToLowerInvariant()))
            {
                filter = filterMatch.Groups[1].Value.ToLowerInvariant();
                i += filterMatch.Length;
            }

            string text;
            var quoted = false;
            if (i < n && query[i] == '"')
            {
                quoted = true;
                var close = query.IndexOf('"', i + 1);
                var end = close == -1 ? n : close;
                text = query[(i + 1)..end];
                i = close == -1 ? n : close + 1;
            }
            else
            {
                var start = i;
                while (i < n && !char.IsWhiteSpace(query[i])) i++;
                text = query[start..i];
            }

            // Collapse inner whitespace so a phrase matches regardless of how it was typed
            text = Whitespace().Replace(text.Trim(), " ");
            // A lone "-" (e.g. "a - b") is punctuation, not a term
            if (!quoted && filter is null && text == "-") continue;
            if (text.Length > 0) tokens.Add(new Token(text, negated, filter, quoted));
        }
        return tokens;
    }

    /// <summary>
    /// Whether a title matches a lower-cased <c>tag:</c>/<c>notebook:</c> pattern: case-insensitive and
    /// whole, with <c>*</c> matching any run of characters.
    /// </summary>
    public static bool MatchesNamePattern(string title, string pattern)
    {
        var lower = title.ToLowerInvariant();
        if (!pattern.Contains('*')) return lower == pattern;
        var regex = "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$";
        return Regex.IsMatch(lower, regex, RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    }

    [GeneratedRegex(@"\G([a-zA-Z]+):(?=\S)")]
    private static partial Regex FilterPrefix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
