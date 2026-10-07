using ChurchYouTubeAssistant.Models.Ai;

namespace ChurchYouTubeAssistant.Ai;

/// <summary>The outcome of one (possibly multi-stage) call to OpenAI for video analysis.</summary>
public sealed record VideoAnalysisAiCallResult
{
    public required VideoAnalysisResult Result { get; init; }

    /// <summary>
    /// The raw JSON OpenAI returned (single-request case), or a synthetic combined JSON document
    /// (multi-stage case), kept for audit/debugging.
    /// </summary>
    public required string RawResponseJson { get; init; }

    public required string Model { get; init; }
    public required string PromptVersion { get; init; }

    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public int? TotalTokens { get; init; }

    public required bool UsedMultiStageFallback { get; init; }
}
