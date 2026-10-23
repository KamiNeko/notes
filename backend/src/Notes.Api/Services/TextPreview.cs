using System.Globalization;
using System.Text.RegularExpressions;

namespace Notes.Api.Services;

/// <summary>Plain-text previews of note bodies for lists and search snippets.</summary>
public static partial class TextPreview
{
    /// <summary><c>MarkupLanguage</c> of HTML notes; Markdown is 1.</summary>
    public const int MarkupHtml = 2;

    public const int DefaultLength = 150;

    /// <summary>Preview of a body in its markup language.</summary>
    public static string For(string? body, int markupLanguage, int maxChars = DefaultLength) =>
        markupLanguage == MarkupHtml ? StripHtml(body, maxChars) : StripMarkdown(body, maxChars);

    /// <summary>Strips Markdown syntax and truncates to <paramref name="maxChars"/> (at a word boundary when possible).</summary>
    public static string StripMarkdown(string? markdown, int maxChars = DefaultLength)
    {
        if (string.IsNullOrEmpty(markdown)) return "";
        var text = markdown;
        text = CodeBlock().Replace(text, "");
        text = InlineCode().Replace(text, "$1");
        text = Image().Replace(text, "");
        text = Link().Replace(text, "$1");
        text = Heading().Replace(text, "");
        text = Blockquote().Replace(text, "");
        text = HorizontalRule().Replace(text, "");
        text = BulletMarker().Replace(text, "");
        text = NumberMarker().Replace(text, "");
        text = Strong().Replace(text, "$2");
        text = Emphasis().Replace(text, "$2");
        text = Strikethrough().Replace(text, "$1");
        text = TableSeparator().Replace(text, "");
        text = TablePipe().Replace(text, " ");
        text = HtmlTag().Replace(text, "");
        text = Whitespace().Replace(text, " ").Trim();
        return Truncate(text, maxChars);
    }

    private static readonly Dictionary<string, string> HtmlEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["quot"] = "\"", ["apos"] = "'", ["nbsp"] = " ",
    };

    /// <summary>Strips HTML (tags, comments, script/style contents), decodes common entities and truncates.</summary>
    public static string StripHtml(string? html, int maxChars = DefaultLength)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var text = html;
        text = HtmlComment().Replace(text, " ");
        text = HtmlHiddenElement().Replace(text, " ");
        // Block-level tags separate words, inline ones (<b>, <a>, <span>…) don't
        text = HtmlBlockTag().Replace(text, " ");
        text = HtmlAnyTag().Replace(text, "");
        text = HtmlEntity().Replace(text, m =>
        {
            var name = m.Groups[1].Value;
            if (name[0] == '#')
            {
                var isHex = name.Length > 1 && (name[1] == 'x' || name[1] == 'X');
                var ok = isHex
                    ? int.TryParse(name.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)
                    : int.TryParse(name.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
                return ok && code > 0 && code <= 0x10ffff && (code < 0xd800 || code > 0xdfff) ? char.ConvertFromUtf32(code) : m.Value;
            }
            return HtmlEntities.TryGetValue(name, out var value) ? value : m.Value;
        });
        text = Whitespace().Replace(text, " ").Trim();
        return Truncate(text, maxChars);
    }

    /// <summary>Cuts text to maxChars, at a word boundary when one is close, and appends "...".</summary>
    public static string Truncate(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        var truncated = text[..maxChars];
        var lastSpace = truncated.LastIndexOf(' ');
        return lastSpace > maxChars * 0.7 ? $"{truncated[..lastSpace]}..." : $"{truncated}...";
    }

    [GeneratedRegex(@"```[\s\S]*?```")]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"^#{1,6}\s+", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^>\s+", RegexOptions.Multiline)]
    private static partial Regex Blockquote();

    [GeneratedRegex(@"^[-*_]{3,}\s*$", RegexOptions.Multiline)]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"^[\*\-+]\s+", RegexOptions.Multiline)]
    private static partial Regex BulletMarker();

    [GeneratedRegex(@"^\d+\.\s+", RegexOptions.Multiline)]
    private static partial Regex NumberMarker();

    [GeneratedRegex(@"(\*\*|__)(.*?)\1")]
    private static partial Regex Strong();

    [GeneratedRegex(@"(\*|_)(.*?)\1")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"~~(.*?)~~")]
    private static partial Regex Strikethrough();

    [GeneratedRegex(@"^\s*\|?(\s*:?-{3,}:?\s*\|)+\s*:?-*:?\s*$", RegexOptions.Multiline)]
    private static partial Regex TableSeparator();

    [GeneratedRegex(@"\s*\|\s*")]
    private static partial Regex TablePipe();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"<!--[\s\S]*?(?:-->|$)")]
    private static partial Regex HtmlComment();

    [GeneratedRegex(@"<(script|style|head|title|noscript)\b[\s\S]*?(?:</\1\s*>|$)", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlHiddenElement();

    [GeneratedRegex(@"</?(?:address|article|aside|blockquote|br|dd|div|dl|dt|figcaption|figure|footer|h[1-6]|header|hr|li|main|nav|ol|p|pre|section|table|td|th|tr|ul)\b[^>]*>?", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBlockTag();

    [GeneratedRegex(@"</?[a-zA-Z][^>]*>?")]
    private static partial Regex HtmlAnyTag();

    [GeneratedRegex(@"&(#x[0-9a-f]+|#\d+|[a-z]+);", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlEntity();
}
