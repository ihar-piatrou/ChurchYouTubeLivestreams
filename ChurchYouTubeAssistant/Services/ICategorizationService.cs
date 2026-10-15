using ChurchYouTubeAssistant.Models;

namespace ChurchYouTubeAssistant.Services;

public sealed record CategorizationResult(TheologyCategory Primary, IReadOnlyList<TheologyCategory> Secondary);

/// <summary>
/// Classifies a sermon into the fixed <see cref="TheologyCategory"/> taxonomy from its already-saved
/// analysis signal (main message, themes, keywords, primary Bible references) - deliberately not the
/// full transcript, so this stays cheap enough to bulk-run against every already-analyzed sermon as
/// a backfill, not just new ones.
/// </summary>
/// <remarks>
/// This is its own interface (not folded into IVideoAnalysisAiService) specifically so the backend
/// can be swapped - today OpenAiCategorizationService, selectable via
/// Configuration/CategorizationOptions to a Jev (TypeSafe AI) implementation once that integration
/// has real credentials - without touching the rest of the analysis pipeline.
/// </remarks>
public interface ICategorizationService
{
    Task<CategorizationResult> CategorizeAsync(VideoAnalysis analysis, CancellationToken cancellationToken = default);
}
