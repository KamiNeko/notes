namespace Notes.Api.Services;

// JSON shapes of the REST API (camelCase on the wire). Ids are 32-hex strings, times Unix ms.

public record NotebookDto(string Id, string Title, string ParentId, List<NotebookDto> Children, int NoteCount, long CreatedTime, long UpdatedTime);

public record TrashedNotebookDto(string Id, string Title, string ParentId, long DeletedTime, int NoteCount, int NotebookCount, int EarlierNoteCount);

public record NotebookSummaryDto(string Id, string Title, string ParentId);

public record CountsDto(int Notebooks, int Notes);

public record TagDto(string Id, string Title, int NoteCount);

public record TagRefDto(string Id, string Title);

public record NoteListItemDto(
    string Id,
    string Title,
    string ParentId,
    string NotebookId,
    long CreatedTime,
    long UpdatedTime,
    long DeletedTime,
    string Preview,
    bool IsTodo,
    bool TodoCompleted,
    long TodoDue);

public record NoteDto(
    string Id,
    string Title,
    string Body,
    string Preview,
    string ParentId,
    string NotebookId,
    string NotebookTitle,
    List<TagRefDto> Tags,
    long CreatedTime,
    long UpdatedTime,
    long DeletedTime,
    bool IsTodo,
    bool TodoCompleted,
    long TodoDue,
    int MarkupLanguage);

public record SearchMatchesDto(bool InTitle, bool InBody, string Snippet, List<string> Terms);

public record SearchResultDto(
    string Id,
    string Title,
    string ParentId,
    string NotebookId,
    long CreatedTime,
    long UpdatedTime,
    long DeletedTime,
    string Preview,
    bool IsTodo,
    bool TodoCompleted,
    long TodoDue,
    SearchMatchesDto Matches);

public record ResourceDto(string Id, string Title, string Mime, long Size);

public record NoteVersionDto(string Id, string NoteId, string Title, string Preview, long UpdatedTime, long CreatedTime);

public record NoteVersionDetailDto(string Id, string NoteId, string Title, string Body, long UpdatedTime, long CreatedTime);

public record UserDto(string Id, string Email, string DisplayName, bool IsAdmin, long CreatedTime);
