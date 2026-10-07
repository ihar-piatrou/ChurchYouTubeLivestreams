namespace ChurchYouTubeAssistant.Models;

/// <summary>Outcome of one AI analysis attempt.</summary>
public enum AnalysisStatus
{
    /// <summary>The OpenAI call is in flight (used only transiently; failures/successes are recorded promptly).</summary>
    Pending,

    /// <summary>The response passed validation with no issues worth flagging.</summary>
    Succeeded,

    /// <summary>The response was usable but one or more sections were dropped/corrected during validation.</summary>
    PartiallyValid,

    /// <summary>The call failed, or the response's core content (title/description) was unusable.</summary>
    Failed
}
