using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Notes.Api.Infrastructure;

namespace Notes.Api.Auth;

/// <summary>
/// Locks an account for 15 minutes after <see cref="NotesOptions.LoginMaxFailures"/> failed sign-ins,
/// counted across all client addresses (the per-address rate limit alone doesn't stop a guesser with
/// many addresses). While locked, sign-ins are refused without checking the password. Kept in memory:
/// a restart forgets the counts.
/// </summary>
public class LoginThrottle(TimeProvider clock, IOptions<NotesOptions> options)
{
    /// <summary>How long failures count, and how long an account stays locked.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset Since)> entries = new();

    private int MaxFailures => Math.Max(1, options.Value.LoginMaxFailures);

    /// <summary>How long the account stays locked, or null when it may sign in.</summary>
    public TimeSpan? LockedFor(string normalizedEmail)
    {
        if (!entries.TryGetValue(normalizedEmail, out var entry)) return null;
        var left = entry.Since + Window - clock.GetUtcNow();
        if (left <= TimeSpan.Zero)
        {
            entries.TryRemove(KeyValuePair.Create(normalizedEmail, entry));
            return null;
        }
        return entry.Failures >= MaxFailures ? left : null;
    }

    /// <summary>Counts a failed sign-in.</summary>
    public void RecordFailure(string normalizedEmail)
    {
        var now = clock.GetUtcNow();
        entries.AddOrUpdate(normalizedEmail, _ => (1, now),
            (_, entry) => entry.Since + Window <= now ? (1, now) : (entry.Failures + 1, entry.Since));
        // Unknown emails are counted too (otherwise the lock would tell which accounts exist), so drop stale ones now and then
        if (entries.Count > 10_000)
        {
            foreach (var (key, entry) in entries)
            {
                if (entry.Since + Window <= now) entries.TryRemove(KeyValuePair.Create(key, entry));
            }
        }
    }

    /// <summary>Forgets the failures after a successful sign-in.</summary>
    public void Reset(string normalizedEmail) => entries.TryRemove(normalizedEmail, out _);
}
