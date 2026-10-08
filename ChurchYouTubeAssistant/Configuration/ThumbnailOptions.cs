namespace ChurchYouTubeAssistant.Configuration;

/// <summary>Which image-generation API thumbnail requests are routed to.</summary>
public enum ThumbnailProvider
{
    Ideogram,
    OpenAi
}

/// <summary>
/// Selects which thumbnail image provider is active, bound from the "Thumbnail" section.
/// </summary>
/// <remarks>
/// Both providers' own options (<see cref="IdeogramOptions"/>, <see cref="OpenAiImageOptions"/>)
/// stay registered regardless of this setting - only the one actually selected here needs its
/// API key configured. Changing this value requires a restart (see Program.cs).
/// </remarks>
public sealed class ThumbnailOptions
{
    public const string SectionName = "Thumbnail";

    public ThumbnailProvider Provider { get; init; } = ThumbnailProvider.Ideogram;
}
