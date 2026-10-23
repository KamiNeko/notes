namespace Notes.Api.Infrastructure;

/// <summary>
/// An error meant for the client: its status code and message are sent as <c>{ error, status }</c>,
/// plus <c>details</c> when set (e.g. the current note on a 409 conflict).
/// </summary>
public class ApiException(int status, string message, object? details = null) : Exception(message)
{
    public int Status { get; } = status;

    public object? Details { get; } = details;

    public static ApiException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);

    public static ApiException NotFound(string message) => new(StatusCodes.Status404NotFound, message);

    public static ApiException Conflict(string message, object? details = null) => new(StatusCodes.Status409Conflict, message, details);
}
