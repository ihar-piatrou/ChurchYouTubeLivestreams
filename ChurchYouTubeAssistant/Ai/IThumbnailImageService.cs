namespace ChurchYouTubeAssistant.Ai;

/// <summary>The generated thumbnail image, ready to store and/or upload to YouTube.</summary>
public sealed record ThumbnailImageResult(byte[] ImageData, string ContentType);

/// <summary>
/// Generates a thumbnail image from a text prompt via whichever provider
/// <see cref="Configuration.ThumbnailOptions.Provider"/> selects - Ideogram
/// (<see cref="IdeogramThumbnailService"/>) or OpenAI's gpt-image-1
/// (<see cref="OpenAiThumbnailImageService"/>). See Program.cs for how the choice is wired up.
/// </summary>
public interface IThumbnailImageService
{
    /// <exception cref="Exceptions.ThumbnailGenerationException">
    /// The provider is not configured, the prompt is empty, or the call/download failed.
    /// </exception>
    Task<ThumbnailImageResult> GenerateAsync(string imagePrompt, CancellationToken cancellationToken = default);
}
