using Microsoft.EntityFrameworkCore;
using Notes.Api.Data;
using Notes.Api.Infrastructure;

namespace Notes.Api.Services;

/// <summary>
/// Search over active notes with the <see cref="SearchQuery"/> syntax. Postgres narrows the notes
/// down (<c>ILIKE</c>, backed by trigram indexes); ranking and snippets are computed here.
/// </summary>
public class SearchService(NotesDbContext db)
{
    /// <summary>Most results returned for one query.</summary>
    public const int MaxResults = 500;

    /// <summary>
    /// Matching notes sorted by relevance (terms in the title ×100, in the body ×10), then newest first.
    /// </summary>
    public async Task<List<SearchResultDto>> SearchAsync(Guid userId, string queryText, CancellationToken ct = default)
    {
        var parsed = SearchQuery.Parse(queryText);
        if (parsed.IsEmpty) return [];

        var query = db.Notes.AsNoTracking().Where(n => n.UserId == userId && n.DeletedTime == null);

        foreach (var term in parsed.Terms)
        {
            var pattern = $"%{EscapeLike(term)}%";
            query = query.Where(n => EF.Functions.ILike(n.Title, pattern, "\\") || EF.Functions.ILike(n.Body, pattern, "\\"));
        }
        foreach (var term in parsed.ExcludedTerms)
        {
            var pattern = $"%{EscapeLike(term)}%";
            query = query.Where(n => !EF.Functions.ILike(n.Title, pattern, "\\") && !EF.Functions.ILike(n.Body, pattern, "\\"));
        }
        foreach (var tag in parsed.Tags)
        {
            var pattern = NamePatternToLike(tag);
            query = query.Where(n => n.NoteTags.Any(nt => EF.Functions.ILike(nt.Tag!.Title, pattern, "\\")));
        }
        foreach (var tag in parsed.ExcludedTags)
        {
            var pattern = NamePatternToLike(tag);
            query = query.Where(n => !n.NoteTags.Any(nt => EF.Functions.ILike(nt.Tag!.Title, pattern, "\\")));
        }
        if (parsed.Notebooks.Count > 0 || parsed.ExcludedNotebooks.Count > 0)
        {
            var notebooks = await db.Notebooks.AsNoTracking()
                .Where(n => n.UserId == userId)
                .Select(n => new { n.Id, n.ParentId, n.Title })
                .ToListAsync(ct);
            var childrenOf = notebooks.Where(n => n.ParentId != null).ToLookup(n => n.ParentId!.Value, n => n.Id);
            HashSet<Guid> Matching(List<string> patterns)
            {
                var result = new HashSet<Guid>();
                var stack = new Stack<Guid>(notebooks
                    .Where(n => patterns.Any(p => SearchQuery.MatchesNamePattern(n.Title, p)))
                    .Select(n => n.Id));
                while (stack.Count > 0)
                {
                    var id = stack.Pop();
                    if (!result.Add(id)) continue;
                    foreach (var child in childrenOf[id]) stack.Push(child);
                }
                return result;
            }
            if (parsed.Notebooks.Count > 0)
            {
                var included = Matching(parsed.Notebooks).ToList();
                query = query.Where(n => included.Contains(n.NotebookId));
            }
            if (parsed.ExcludedNotebooks.Count > 0)
            {
                var excluded = Matching(parsed.ExcludedNotebooks).ToList();
                query = query.Where(n => !excluded.Contains(n.NotebookId));
            }
        }
        if (parsed.Type == "todo" || parsed.Completed is not null) query = query.Where(n => n.IsTodo);
        if (parsed.Type == "note") query = query.Where(n => !n.IsTodo);
        if (parsed.Completed is { } completed)
        {
            query = completed ? query.Where(n => n.TodoCompletedTime != null) : query.Where(n => n.TodoCompletedTime == null);
        }

        var notes = await query.ToListAsync(ct);
        return notes
            .Select(n => ToResult(n, parsed))
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Result.UpdatedTime)
            .Take(MaxResults)
            .Select(r => r.Result)
            .ToList();
    }

    private static (int Score, SearchResultDto Result) ToResult(Note note, SearchQuery parsed)
    {
        var titleLower = note.Title.ToLowerInvariant();
        var bodyLower = note.Body.ToLowerInvariant();
        var titleHits = parsed.Terms.Count(t => titleLower.Contains(t, StringComparison.Ordinal));
        var bodyHits = parsed.Terms.Count(t => bodyLower.Contains(t, StringComparison.Ordinal));

        // Snippet around the earliest occurrence of any term in the body
        int matchIndex = -1, matchLength = 0;
        foreach (var term in parsed.Terms)
        {
            var index = bodyLower.IndexOf(term, StringComparison.Ordinal);
            if (index != -1 && (matchIndex == -1 || index < matchIndex))
            {
                matchIndex = index;
                matchLength = term.Length;
            }
        }
        var snippet = "";
        // Lower-casing can change the length in rare cases; only cut when the positions still fit
        if (matchIndex != -1 && bodyLower.Length == note.Body.Length)
        {
            var start = Math.Max(0, matchIndex - 60);
            var end = Math.Min(note.Body.Length, matchIndex + matchLength + 60);
            snippet = $"{(start > 0 ? "..." : "")}{TextPreview.For(note.Body[start..end], note.MarkupLanguage)}{(end < note.Body.Length ? "..." : "")}";
        }

        var result = new SearchResultDto(
            Ids.Format(note.Id), note.Title, Ids.Format(note.NotebookId), Ids.Format(note.NotebookId),
            Times.ToMs(note.CreatedTime), Times.ToMs(note.UpdatedTime), 0, note.Preview, note.IsTodo,
            note.TodoCompletedTime != null, Times.ToMs(note.TodoDueTime),
            new SearchMatchesDto(titleHits > 0, matchIndex != -1, snippet, parsed.Terms));
        return (titleHits * 100 + bodyHits * 10, result);
    }

    /// <summary>Escapes <c>%</c>, <c>_</c> and the escape character itself for a LIKE pattern.</summary>
    internal static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>A whole-title <c>tag:</c>/<c>notebook:</c> pattern with <c>*</c> wildcards as a LIKE pattern.</summary>
    internal static string NamePatternToLike(string pattern) =>
        string.Join("%", pattern.Split('*').Select(EscapeLike));
}
