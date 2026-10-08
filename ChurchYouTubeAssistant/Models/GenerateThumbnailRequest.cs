namespace ChurchYouTubeAssistant.Models;

/// <summary>The (possibly hand-edited) image prompt to generate a thumbnail from.</summary>
public sealed record GenerateThumbnailRequest
{
    public required string ImagePrompt { get; init; }
}
