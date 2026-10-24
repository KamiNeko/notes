using Notes.Api.Services;

namespace Notes.Api.Tests;

public class SearchQueryTests
{
    [Fact]
    public void Parses_terms_phrases_negation_and_filters()
    {
        var q = SearchQuery.Parse("Shopping  \"Grocery   List\" -milk -\"two words\" tag:Food -tag:old notebook:\"My Recipes\" type:todo iscompleted:0");
        Assert.Equal(["shopping", "grocery list"], q.Terms);
        Assert.Equal(["milk", "two words"], q.ExcludedTerms);
        Assert.Equal(["food"], q.Tags);
        Assert.Equal(["old"], q.ExcludedTags);
        Assert.Equal(["my recipes"], q.Notebooks);
        Assert.Equal("todo", q.Type);
        Assert.False(q.Completed);
    }

    [Fact]
    public void Unknown_filters_and_invalid_values_are_words()
    {
        var q = SearchQuery.Parse("foo:bar type:x iscompleted:2 a - b \"unclosed phrase");
        Assert.Equal(["foo:bar", "type:x", "iscompleted:2", "a", "b", "unclosed phrase"], q.Terms);
        Assert.Null(q.Type);
        Assert.True(SearchQuery.Parse("  ").IsEmpty);
        Assert.False(SearchQuery.Parse("type:note").IsEmpty);
    }

    [Theory]
    [InlineData("Projects", "projects", true)]
    [InlineData("Projects", "proj*", true)]
    [InlineData("Projects", "*ject*", true)]
    [InlineData("Projects", "proj", false)]
    [InlineData("a.b", "a.*", true)]
    [InlineData("axb", "a.b", false)]
    public void Name_patterns_match_whole_titles(string title, string pattern, bool expected) =>
        Assert.Equal(expected, SearchQuery.MatchesNamePattern(title, pattern));
}

public class TextPreviewTests
{
    [Fact]
    public void Strips_markdown()
    {
        const string body = "# Title\n\n> quote\n\n- [x] done\n1. first\n\n```js\ncode()\n```\n\n![img](:/abc) **bold** _it_ ~~gone~~ `code` [link](x)\n\n| a | b |\n| --- | --- |\n| 1 | 2 |";
        Assert.Equal("Title quote [x] done first bold it gone code link a b 1 2", TextPreview.StripMarkdown(body));
    }

    [Fact]
    public void Strips_html_and_decodes_entities()
    {
        Assert.Equal("Head Hello world & more 😀", TextPreview.StripHtml("<h1>Head</h1><style>p{}</style><p>Hello <b>world</b> &amp; more &#x1F600;</p><!-- c -->"));
    }

    [Fact]
    public void Truncates_at_a_word_boundary()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 60));
        var preview = TextPreview.StripMarkdown(text);
        Assert.True(preview.Length <= 153);
        Assert.EndsWith("word...", preview);
    }
}

public class ResourceTypesTests
{
    [Theory]
    [InlineData("image/png", "image/png", true)]
    [InlineData("video/mp4", "video/mp4", true)]
    [InlineData("image/svg+xml", "image/svg+xml", false)]
    [InlineData("text/html", "application/octet-stream", false)]
    [InlineData("application/javascript", "application/octet-stream", false)]
    [InlineData("bogus", "application/octet-stream", false)]
    public void Only_safe_types_are_inline(string mime, string contentType, bool inline) =>
        Assert.Equal((contentType, inline), ResourceTypes.ResponseType(mime));

    [Fact]
    public void File_names_are_cleaned()
    {
        Assert.Equal("a_b_c.txt", ResourceTypes.CleanFileName("a/b\\c.txt"));
        Assert.Equal("file", ResourceTypes.CleanFileName("\u0001"));
        Assert.Equal("attachment; filename=\"_x_.txt\"; filename*=UTF-8''%22x%22.txt", ResourceTypes.ContentDisposition(false, "\"x\".txt"));
    }
}

public class TotpTests
{
    // RFC 6238 appendix B (SHA-1), last six of the eight digits
    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1234567890, "005924")]
    [InlineData(2000000000, "279037")]
    public void Codes_match_the_rfc_test_vectors(long unixSeconds, string expected)
    {
        var secret = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal(expected, Notes.Api.Auth.Totp.Code(secret, Notes.Api.Auth.Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))));
    }

    [Fact]
    public void Neighbouring_steps_match_once_and_others_never()
    {
        var secret = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var step = Notes.Api.Auth.Totp.StepAt(now);
        Assert.Equal(step - 1, Notes.Api.Auth.Totp.Match(secret, Notes.Api.Auth.Totp.Code(secret, step - 1), now, 0));
        Assert.Equal(step + 1, Notes.Api.Auth.Totp.Match(secret, Notes.Api.Auth.Totp.Code(secret, step + 1), now, 0));
        Assert.Null(Notes.Api.Auth.Totp.Match(secret, Notes.Api.Auth.Totp.Code(secret, step + 2), now, 0));
        Assert.Null(Notes.Api.Auth.Totp.Match(secret, Notes.Api.Auth.Totp.Code(secret, step), now, step));
        Assert.Null(Notes.Api.Auth.Totp.Match(secret, "12345", now, 0));
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Notes.Api.Auth.Totp.ToBase32(secret));
    }
}
