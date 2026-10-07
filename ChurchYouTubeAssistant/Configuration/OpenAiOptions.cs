namespace ChurchYouTubeAssistant.Configuration;

/// <summary>
/// Strongly typed OpenAI configuration, bound from the "OpenAI" section.
/// </summary>
/// <remarks>
/// <see cref="ApiKey"/> must come from a secret source (.NET User Secrets locally, environment
/// variables / Key Vault / Secrets Manager when hosted) - never from appsettings.json.
/// </remarks>
public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    /// <summary>Supplied via User Secrets / environment, never source control.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Chat model used for video analysis. Configurable (not hardcoded) because OpenAI's model
    /// lineup and recommended defaults change over time; verify the current recommended model in
    /// OpenAI's own docs before relying on this default.
    /// </summary>
    public string Model { get; init; } = "gpt-5";

    /// <summary>
    /// Transcripts longer than roughly this many estimated tokens use the multi-stage fallback
    /// (chunked extraction + synthesis) instead of one single-request analysis.
    /// </summary>
    public int SingleRequestTokenBudget { get; init; } = 60_000;

    /// <summary>
    /// Upper bound on tokens the model is allowed to generate for one analysis.
    /// </summary>
    /// <remarks>
    /// For reasoning models (the gpt-5 family), hidden reasoning tokens are drawn from this SAME
    /// budget - a hard task can exhaust it entirely before any visible JSON is written, producing
    /// an empty response with FinishReason=Length rather than a usable (if truncated) answer.
    /// OpenAI's own guidance is to reserve at least 25,000 tokens for reasoning + output combined;
    /// 32,000 leaves real headroom above that floor for the large structured document this feature
    /// asks for. See also <see cref="ReasoningEffort"/>.
    /// </remarks>
    public int MaxOutputTokens { get; init; } = 32_000;

    /// <summary>
    /// "minimal", "low", "medium" or "high" - how much hidden reasoning the model does before
    /// writing its visible output. Lower values leave more of <see cref="MaxOutputTokens"/> free
    /// for the actual JSON content, at some cost to analysis depth. "high" (closer to the model's
    /// own default for hard tasks) is what produced empty responses in practice for a full sermon
    /// analysis; "medium" is a safer default for this feature's token budget.
    /// </summary>
    /// <remarks>
    /// Maps to OpenAI's <c>ChatReasoningEffortLevel</c>, which is an experimental SDK API
    /// (OPENAI001) that may change; see the usage site in OpenAiVideoAnalysisService.
    /// </remarks>
    public string ReasoningEffort { get; init; } = "medium";

    /// <summary>
    /// How long a single OpenAI call is allowed to run before being cancelled. Also set as the
    /// OpenAI SDK's own ClientPipelineOptions.NetworkTimeout (see Program.cs) - the SDK's default
    /// is only 100 seconds, which a full sermon analysis (long transcript in, a large structured
    /// JSON document out) routinely exceeds. 10 minutes gives real headroom; raise it further if
    /// your sermons are unusually long or the configured model is slow.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(10);
}
