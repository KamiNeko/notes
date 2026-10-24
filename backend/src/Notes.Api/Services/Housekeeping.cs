using Microsoft.Extensions.Options;
using Notes.Api.Infrastructure;

namespace Notes.Api.Services;

/// <summary>
/// Daily cleanup: deletes versions past <see cref="NotesOptions.VersionRetentionDays"/> and empties
/// trash older than <see cref="NotesOptions.TrashRetentionDays"/> (each only when configured).
/// </summary>
public class Housekeeping(IServiceScopeFactory scopes, IOptions<NotesOptions> options, TimeProvider clock, ILogger<Housekeeping> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.VersionRetentionDays <= 0 && options.Value.TrashRetentionDays <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), clock);
        do
        {
            await RunAsync("Pruning note versions", async services =>
            {
                var deleted = await services.GetRequiredService<VersionService>().PruneAsync(clock.GetUtcNow(), stoppingToken);
                if (deleted > 0) logger.LogInformation("Deleted {Count} note versions past the retention period", deleted);
            });
            await RunAsync("Emptying the trash", async services =>
            {
                var deleted = await services.GetRequiredService<TrashService>().EmptyExpiredAsync(clock.GetUtcNow(), stoppingToken);
                if (deleted.Notebooks + deleted.Notes > 0)
                {
                    logger.LogInformation("Deleted {Notebooks} notebooks and {Notes} notes that were in the trash past the retention period", deleted.Notebooks, deleted.Notes);
                }
            });
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Runs one job in its own scope; a failure is logged and doesn't stop the others.</summary>
    private async Task RunAsync(string job, Func<IServiceProvider, Task> work)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await work(scope.ServiceProvider);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(error, "{Job} failed", job);
        }
    }
}
