namespace ChurchYouTubeAssistant.Exceptions;

/// <summary>Classification of AI analysis failures, used to pick an HTTP status and message.</summary>
public enum AiAnalysisError
{
    /// <summary>OpenAI API key missing/invalid, or another configuration problem.</summary>
    Configuration,

    /// <summary>OpenAI rate-limited the request (HTTP 429).</summary>
    RateLimited,

    /// <summary>The request exceeded <see cref="Configuration.OpenAiOptions.RequestTimeout"/>.</summary>
    Timeout,

    /// <summary>
    /// The model returned something that did not deserialize or did not pass validation
    /// (missing required fields, invalid timestamps, etc.) after exhausting retries.
    /// </summary>
    InvalidResponse,

    /// <summary>Any other OpenAI-side failure (5xx, network, etc.).</summary>
    TransientFailure
}

/// <summary>A failure calling or interpreting the OpenAI video-analysis request.</summary>
public sealed class AiAnalysisException(AiAnalysisError error, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public AiAnalysisError Error { get; } = error;
}
