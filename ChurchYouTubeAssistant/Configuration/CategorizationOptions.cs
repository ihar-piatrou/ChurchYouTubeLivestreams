namespace ChurchYouTubeAssistant.Configuration;

public enum CategorizationProvider
{
    OpenAi,
    Jev
}

/// <summary>
/// Selects which backend answers "what theology category is this sermon" (see
/// Services/ICategorizationService.cs). Same provider-switch pattern as
/// ThumbnailOptions/ThumbnailProvider.
/// </summary>
public sealed class CategorizationOptions
{
    public const string SectionName = "Categorization";

    public CategorizationProvider Provider { get; init; } = CategorizationProvider.OpenAi;
}
