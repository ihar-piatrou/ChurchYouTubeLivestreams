using ChurchYouTubeAssistant.Configuration;
using Microsoft.Extensions.Options;

namespace ChurchYouTubeAssistant.Services;

/// <summary>
/// Runs work items from <see cref="IBackgroundTaskQueue"/> using a small pool of concurrent
/// workers (<see cref="BackgroundJobOptions.MaxConcurrency"/>, default 3) for the process's
/// lifetime. Concurrency is deliberately bounded rather than unlimited: this is a single admin's
/// tool, and running everything in parallel would let a bulk "Analyze selected" batch fire off
/// dozens of simultaneous OpenAI calls and blow through rate limits.
/// </summary>
public sealed class QueuedHostedService(
    IBackgroundTaskQueue taskQueue,
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobOptions> options,
    ILogger<QueuedHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerCount = Math.Max(1, options.Value.MaxConcurrency);
        var workers = Enumerable.Range(0, workerCount).Select(_ => RunWorkerAsync(stoppingToken));
        return Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
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
