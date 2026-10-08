namespace ChurchYouTubeAssistant.Configuration;

/// <summary>
/// Strongly typed OpenAI image-generation configuration, bound from the "OpenAiImage" section.
/// </summary>
/// <remarks>
/// Reuses <see cref="OpenAiOptions.ApiKey"/> for credentials - it's the same OpenAI account as
/// the chat/analysis calls, so there is no separate API key to configure here.
/// </remarks>
public sealed class OpenAiImageOptions
{
    public const string SectionName = "OpenAiImage";

    /// <summary>
    /// The image model to call. "gpt-image-2.5-sunburst" is OpenAI's current precision-oriented
    /// model (as of the Sept 2026 GPT Image 2.5 release) - the lineage is gpt-image-1 ->
    /// gpt-image-1.5 -> gpt-image-2 -> gpt-image-2.5, each generation superseding the last.
    /// Sunburst specifically targets accurate text rendering and intricate layouts, which is the
    /// reason this provider exists alongside Ideogram; "gpt-image-2.5-flare" is OpenAI's faster,
    /// lower-fidelity sibling for routine (non-text-critical) generation, not used here.
    /// </summary>
    public string Model { get; init; } = "gpt-image-2.5-sunburst";

    /// <summary>
    /// A size understood by the configured model, as "WIDTHxHEIGHT" or "auto". GPT Image 2.5
    /// accepts arbitrary resolutions subject to its own constraints: both edges a multiple of 16,
    /// the long:short edge ratio no more than 3:1, and total pixels between 655,360 and 8,294,400.
    /// "1280x720" - YouTube's own standard thumbnail resolution - is an exact 16:9 size that
    /// satisfies all three, which is why it's the default rather than a size merely close to 16:9.
    /// Not validated here; an unsupported value surfaces as an OpenAI API error at call time.
    /// </summary>
    public string Size { get; init; } = "1280x720";

    /// <summary>
    /// A quality level understood by the configured model: "low", "medium", "high", "xhigh", or
    /// "max" are GPT Image 2.5's tiers (plus "auto"); gpt-image-1 only understands low/medium/
    /// high/auto. Text accuracy - the reason this app generates thumbnails at all - degrades at
    /// lower quality, so this defaults to "high" rather than trading quality for speed/cost. Not
    /// validated here; an unsupported value surfaces as an OpenAI API error at call time.
    /// </summary>
    public string Quality { get; init; } = "high";

    /// <summary>How long a single image-generation call is allowed to run before being cancelled.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);
}
