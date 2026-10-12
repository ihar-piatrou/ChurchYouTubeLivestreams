namespace ChurchYouTubeAssistant.Services;

/// <summary>
/// A FIFO queue of work items for <see cref="QueuedHostedService"/> to run outside the HTTP
/// request that enqueued them. Each work item receives its own <see cref="IServiceProvider"/>
/// scope (so scoped services like the DbContext resolve correctly) and a cancellation token tied
/// to application shutdown, not to the original request.
/// </summary>
public interface IBackgroundTaskQueue
{
    void Enqueue(Func<IServiceProvider, CancellationToken, Task> workItem);

    Task<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
}
