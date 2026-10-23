namespace Notes.Api.Infrastructure;

/// <summary>Conversions between stored times and the API's Unix milliseconds.</summary>
public static class Times
{
    /// <summary>
    /// The current time cut to whole milliseconds, so a time sent to the client and back as
    /// <c>baseUpdatedTime</c> compares equal to the stored one.
    /// </summary>
    public static DateTimeOffset Now(TimeProvider clock) => FromMs(clock.GetUtcNow().ToUnixTimeMilliseconds());

    public static long ToMs(DateTimeOffset time) => time.ToUnixTimeMilliseconds();

    /// <summary>Unix ms, 0 for null.</summary>
    public static long ToMs(DateTimeOffset? time) => time?.ToUnixTimeMilliseconds() ?? 0;

    public static DateTimeOffset FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms);
}
