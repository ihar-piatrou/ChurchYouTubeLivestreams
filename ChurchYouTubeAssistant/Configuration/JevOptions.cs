namespace ChurchYouTubeAssistant.Configuration;

/// <summary>
/// Strongly typed Jev (TypeSafe AI "System One" classifier, https://docs.typesafe.ai) configuration,
/// bound from the "Jev" section.
/// </summary>
/// <remarks>
/// <see cref="ApiKey"/> must come from a secret source (.NET User Secrets locally), never
/// appsettings.json - same pattern as OpenAiOptions/IdeogramOptions.
/// Deliberately NOT validated on startup: this provider is only used when
/// CategorizationOptions.Provider selects it, and the app must keep starting normally with no Jev
/// key set otherwise - see <see cref="IsConfigured"/> and JevCategorizationService for the
/// point-of-use check instead.
/// </remarks>
public sealed class JevOptions
{
    public const string SectionName = "Jev";

    /// <summary>Supplied via User Secrets / environment, never source control. Empty until configured.</summary>
    public string ApiKey { get; init; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>The model alias used in every request's "model" field (POST /v1/systemone).</summary>
    public string Model { get; init; } = "jev-latest";

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
