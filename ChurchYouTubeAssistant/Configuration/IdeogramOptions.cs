namespace ChurchYouTubeAssistant.Configuration;

/// <summary>
/// Strongly typed Ideogram (text-to-image) configuration, bound from the "Ideogram" section.
/// </summary>
/// <remarks>
/// <see cref="ApiKey"/> must come from a secret source (.NET User Secrets locally, environment
/// variables / Key Vault / Secrets Manager when hosted) - never from appsettings.json. Same pattern
/// as <see cref="OpenAiOptions"/>.
/// <para>
/// Unlike <see cref="OpenAiOptions"/>, this is deliberately NOT validated on startup
/// (no <c>ValidateOnStart</c>): thumbnail image generation itself isn't implemented yet - this
/// class only exists so the key can be configured and read in advance. The app must keep starting
/// normally without an Ideogram key set. Once a service is built that actually calls Ideogram, that
/// service should check <see cref="IsConfigured"/> and fail clearly at the point of use (e.g. when
/// "Generate Thumbnail Image" is clicked), not block the whole app from starting over an unused key.
/// </para>
/// </remarks>
public sealed class IdeogramOptions
{
    public const string SectionName = "Ideogram";

    /// <summary>Supplied via User Secrets / environment, never source control. Empty until configured.</summary>
    public string ApiKey { get; init; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// The model name used in the request path (POST /v2/image/generate/{Model}). "ideogram-4-5"
    /// is the current flagship model; configurable here in case a cheaper/older model (e.g.
    /// "ideogram-4" or "ideogram-3") is ever preferred.
    /// </summary>
    public string Model { get; init; } = "ideogram-4-5";

    /// <summary>
    /// Ideogram v2's "size" parameter: an exact "WIDTHxHEIGHT" pixel size (not an aspect-ratio
    /// keyword like v1/v3 used). 1280x720 is YouTube's standard thumbnail resolution, 16:9.
    /// </summary>
    public string Size { get; init; } = "1280x720";

    /// <summary>
    /// Ideogram v2's "quality" parameter: "very_low", "medium" or "high". Text accuracy - the
    /// reason this app generates thumbnails at all - degrades noticeably at lower quality, so this
    /// defaults to "high" rather than trading quality for speed/cost.
    /// </summary>
    public string Quality { get; init; } = "high";

    /// <summary>How long a single image-generation call is allowed to run before being cancelled.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);
}
