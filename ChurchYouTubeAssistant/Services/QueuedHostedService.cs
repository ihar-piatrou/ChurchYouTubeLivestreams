namespace ChurchYouTubeAssistant.Services;

/// <summary>
/// Runs work items from <see cref="IBackgroundTaskQueue"/> one at a time for the process's
/// lifetime. One at a time is intentional: this is a single admin's tool, not a worker pool, and
/// running analyses sequentially avoids several concurrent OpenAI calls competing for the same
/// rate limits.
/// </summary>
public sealed class QueuedHostedService(
    IBackgroundTaskQueue taskQueue,
    IServiceScopeFactory scopeFactory,
    ILogger<QueuedHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var workItem = await taskQueue.DequeueAsync(stoppingToken);

            try
            {
                using var scope = scopeFactory.CreateScope();
                await workItem(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // App is shutting down - the item is dropped, which is fine for an admin tool.
            }
            catch (Exception ex)
            {
                // The individual work items already catch and record their own expected failures
                // (AI/YouTube exceptions) as a result redirect - reaching here means something
                // unexpected slipped through, which must not take the whole queue down with it.
                logger.LogError(ex, "A background job threw an unhandled exception.");
            }
        }
    }
}
