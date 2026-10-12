using System.Threading.Channels;

namespace ChurchYouTubeAssistant.Services;

/// <inheritdoc cref="IBackgroundTaskQueue"/>
/// <remarks>
/// Unbounded: this app's only producers are admin-triggered "Analyze"/"Generate Thumbnail" clicks,
/// nowhere near enough volume to need back-pressure. If that changes, a bounded channel with
/// <see cref="BoundedChannelFullMode.Wait"/> would be the fix.
/// </remarks>
public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel =
        Channel.CreateUnbounded<Func<IServiceProvider, CancellationToken, Task>>();

    public void Enqueue(Func<IServiceProvider, CancellationToken, Task> workItem) =>
        _channel.Writer.TryWrite(workItem);

    public async Task<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(
        CancellationToken cancellationToken) =>
        await _channel.Reader.ReadAsync(cancellationToken);
}
