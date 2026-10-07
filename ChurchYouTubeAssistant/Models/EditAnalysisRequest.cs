namespace ChurchYouTubeAssistant.Models;

/// <summary>
/// An admin-edited title/description to save as a draft against an analysis, without publishing
/// anything to YouTube. Persisted into <see cref="VideoAnalysis.EditedTitle"/> /
/// <see cref="VideoAnalysis.EditedDescription"/>, kept separate from what the AI originally
/// generated (<see cref="VideoAnalysis.OptimizedTitle"/> / <see cref="VideoAnalysis.OptimizedDescription"/>).
/// </summary>
public sealed record EditAnalysisRequest
{
    public required string Title { get; init; }
    public required string Description { get; init; }
}
