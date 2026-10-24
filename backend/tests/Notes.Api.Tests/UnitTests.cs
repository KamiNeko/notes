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

public class ImportParsingTests
{
    [Fact]
    public void Front_matter_is_read_like_the_export_writes_it()
    {
        var (front, body) = FrontMatter.Parse("---\r\ntitle: \"Say \\\"hi\\\": now\"\r\ncreated: 2026-01-02T03:04:05.000Z\r\ntags:\r\n  - \"a\"\r\n  - b\r\ncompleted?: yes\r\ndue: 2026-02-01T00:00:00.000Z\r\n---\r\n\r\nBody\n---\nmore");
        Assert.Equal("Say \"hi\": now", front.Title);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), front.Created);
        Assert.Null(front.Updated);
        Assert.Equal(["a", "b"], front.Tags);
        Assert.True(front.Completed);
        Assert.Equal(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), front.Due);
        Assert.Equal("Body\n---\nmore", body);
    }

    [Theory]
    [InlineData("No front matter", "No front matter")]
    [InlineData("---\nunclosed: yes\nbody", "---\nunclosed: yes\nbody")]
    [InlineData("---\n---\nBody", "Body")]
    public void Files_without_valid_front_matter_are_all_body(string text, string expectedBody)
    {
        var (front, body) = FrontMatter.Parse(text);
        Assert.Null(front.Title);
        Assert.Equal(expectedBody, body);
    }

    [Theory]
    [InlineData("a/b.md", "a/b.md")]
    [InlineData("a\\b\\c.md", "a/b/c.md")]
    [InlineData("./a//b.md", "a/b.md")]
    [InlineData("../a.md", null)]
    [InlineData("a/../../b.md", null)]
    [InlineData("/etc/x.md", null)]
    [InlineData("C:/x.md", null)]
    [InlineData("a/\u0001.md", null)]
    public void Entry_paths_are_normalized_or_refused(string name, string? expected) =>
        Assert.Equal(expected, ImportService.NormalizePath(name));
}
