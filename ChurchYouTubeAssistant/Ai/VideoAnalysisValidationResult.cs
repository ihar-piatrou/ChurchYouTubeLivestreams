using ChurchYouTubeAssistant.Models.Ai;

namespace ChurchYouTubeAssistant.Ai;

/// <summary>
/// The outcome of validating/cleaning a model-produced <see cref="VideoAnalysisResult"/>.
/// </summary>
/// <param name="Cleaned">
/// The result with invalid individual items (bad chapters, oversized Shorts, etc.) removed and
/// mechanical issues (duration mismatches, out-of-range scores) corrected. Always non-null, even
/// when <see cref="Errors"/> is non-empty, so the caller can still store/inspect what was usable.
/// </param>
/// <param name="Warnings">Non-fatal adjustments made (an item was dropped, a value was clamped, etc.).</param>
/// <param name="Errors">
/// Problems serious enough that the analysis as a whole should not be treated as a clean success
/// (e.g. the title and description were both empty). Non-empty does not mean "discard the result" -
/// it means "mark this analysis as PartiallyValid/Failed rather than Succeeded."
/// </param>
public sealed record VideoAnalysisValidationResult(
    VideoAnalysisResult Cleaned,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
