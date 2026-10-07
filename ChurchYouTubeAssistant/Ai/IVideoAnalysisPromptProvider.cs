namespace ChurchYouTubeAssistant.Ai;

/// <summary>
/// Supplies the current versioned system prompt and JSON schema for video analysis, loaded from
/// plain content files under Prompts/, not baked into application code.
/// </summary>
public interface IVideoAnalysisPromptProvider
{
    /// <summary>Short version tag (e.g. "v1"), stored alongside every analysis for auditability.</summary>
    string Version { get; }

    string SystemPrompt { get; }

    /// <summary>The strict JSON Schema document (as a raw JSON string) passed to OpenAI's structured outputs.</summary>
    string JsonSchema { get; }
}
