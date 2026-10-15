using System.ClientModel;
using Polly;
using Polly.Retry;

namespace ChurchYouTubeAssistant.Infrastructure;

/// <summary>
/// Shared Polly retry pipeline for direct OpenAI SDK calls (chat completions, image generation,
/// categorization). All three call sites (<see cref="Ai.OpenAiVideoAnalysisService"/>,
/// <see cref="Services.OpenAiCategorizationService"/>, <see cref="Ai.OpenAiThumbnailImageService"/>)
/// share the same OpenAI account and therefore the same per-minute/per-day rate limits, so a
/// retryable failure (see <see cref="RetryableStatusCodes"/>) from any of them benefits from the
/// same backoff before giving up.
/// </summary>
/// <remarks>
/// The OpenAI SDK's own built-in retry is explicitly disabled where the <c>ChatClient</c>/
/// <c>ImageClient</c> are registered (see Program.cs) - this pipeline is the single place that
/// decides how an OpenAI call gets retried, instead of two independent retry mechanisms stacking.
/// </remarks>
public static class OpenAiRetryPolicy
{
    private const int MaxRetryAttempts = 5;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// HTTP statuses worth retrying: 429 (rate limited or quota exceeded - OpenAI returns both
    /// under the same status) plus the classic transient 5xx server errors (internal error, bad
    /// gateway, service unavailable, gateway timeout) and 529 ("model overloaded", used by some
    /// OpenAI-compatible backends). A 429 caused by a genuinely exhausted billing quota will not
    /// actually be fixed by waiting and retrying, but there is no reliable way to distinguish that
    /// from ordinary per-minute rate limiting from the HTTP status alone, so both are treated the
    /// same here - worst case, a quota failure just takes the full backoff before reporting, same
    /// as it would without this pipeline.
    /// </summary>
    private static readonly HashSet<int> RetryableStatusCodes = [429, 500, 502, 503, 504, 529];

    /// <summary>
    /// Retries up to 5 times with exponential backoff (2s, 4s, 8s, 16s, 32s) on the status codes in
    /// <see cref="RetryableStatusCodes"/>, and only those. Other failures (bad API key, invalid
    /// request/moderation rejection, timeout) are never going to succeed just by waiting and
    /// retrying, so they are left to fail immediately, same as before this pipeline existed.
    /// </summary>
    public static ResiliencePipeline Create(ILogger logger) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<ClientResultException>(
                    ex => RetryableStatusCodes.Contains(ex.Status)),
                MaxRetryAttempts = MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = BaseDelay,
                UseJitter = false,
                OnRetry = args =>
                {
                    var status = (args.Outcome.Exception as ClientResultException)?.Status;
                    logger.LogWarning(
                        "OpenAI call failed (HTTP {Status}); retrying in {Delay}s (attempt {Attempt}/{Max}).",
                        status, args.RetryDelay.TotalSeconds, args.AttemptNumber + 1, MaxRetryAttempts);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
}
