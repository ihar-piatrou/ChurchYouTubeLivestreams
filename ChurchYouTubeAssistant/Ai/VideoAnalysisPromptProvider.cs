namespace ChurchYouTubeAssistant.Ai;

/// <summary>
/// Reads the current prompt version's files once at startup and holds them in memory.
/// </summary>
/// <remarks>
/// Bumping the prompt means adding a new "Prompts/VideoAnalysisVN" folder and pointing
/// <see cref="CurrentFolderName"/> at it - the old folder stays on disk (and in git history)
/// so a stored analysis's PromptVersion always remains meaningful, even after the prompt changes.
/// </remarks>
public sealed class VideoAnalysisPromptProvider : IVideoAnalysisPromptProvider
{
    public const string CurrentVersion = "v3";
    private const string CurrentFolderName = "VideoAnalysisV3";

    public string Version => CurrentVersion;
    public string SystemPrompt { get; }
    public string JsonSchema { get; }

    public VideoAnalysisPromptProvider(IHostEnvironment env)
    {
        var root = Path.Combine(env.ContentRootPath, "Prompts", CurrentFolderName);

        SystemPrompt = File.ReadAllText(Path.Combine(root, "system-prompt.txt"));
        JsonSchema = File.ReadAllText(Path.Combine(root, "schema.json"));
    }
}
