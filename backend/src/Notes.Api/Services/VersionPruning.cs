using Microsoft.Extensions.Options;
using Notes.Api.Infrastructure;

namespace Notes.Api.Services;

/// <summary>Deletes versions past <see cref="NotesOptions.VersionRetentionDays"/> once a day (when configured).</summary>
public class VersionPruning(IServiceScopeFactory scopes, IOptions<NotesOptions> options, TimeProvider clock, ILogger<VersionPruning> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.VersionRetentionDays <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var deleted = await scope.ServiceProvider.GetRequiredService<VersionService>().PruneAsync(clock.GetUtcNow(), stoppingToken);
                if (deleted > 0) logger.LogInformation("Deleted {Count} note versions past the retention period", deleted);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "Pruning note versions failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
