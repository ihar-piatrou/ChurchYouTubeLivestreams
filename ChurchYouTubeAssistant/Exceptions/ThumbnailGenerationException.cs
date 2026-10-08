namespace ChurchYouTubeAssistant.Exceptions;

/// <summary>Classification of thumbnail image generation failures.</summary>
public enum ThumbnailGenerationError
{
    /// <summary>Ideogram:ApiKey missing/invalid.</summary>
    Configuration,

    /// <summary>Ideogram rate-limited the request.</summary>
    RateLimited,

    /// <summary>The request exceeded Ideogram:RequestTimeout.</summary>
    Timeout,

    /// <summary>Ideogram's response did not contain a usable image.</summary>
    InvalidResponse,

    /// <summary>Any other failure (5xx, network, the analysis has no image prompt yet, etc.).</summary>
    TransientFailure
}

/// <summary>A failure generating or downloading a thumbnail image via Ideogram.</summary>
public sealed class ThumbnailGenerationException(
    ThumbnailGenerationError error, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public ThumbnailGenerationError Error { get; } = error;
}
