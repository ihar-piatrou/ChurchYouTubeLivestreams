namespace ChurchYouTubeAssistant.Models.ViewModels;

/// <summary>
/// View model for the admin dashboard (Home/Index). Separate from the API's DTOs on purpose: this
/// shape exists to render HTML (flash messages, "connect" vs "connected" states), not to be
/// serialized as a contract for another program to consume.
/// </summary>
public sealed class DashboardViewModel
{
    public required bool Connected { get; init; }
    public required bool CanRefreshUnattended { get; init; }

    /// <summary>Non-fatal note from the connection status, e.g. "no refresh token was issued".</summary>
    public string? StatusMessage { get; init; }

    // One-shot messages carried across the OAuth redirect via query string (see OAuthController).
    public string? FlashSuccess { get; init; }
    public string? FlashError { get; init; }
    public IReadOnlyList<string> FlashWarnings { get; init; } = [];

    public YouTubeChannelDto? Channel { get; init; }
    public IReadOnlyList<YouTubeVideoDto> Videos { get; init; } = [];

    /// <summary>Per-video AI-analysis/publish status, keyed by VideoId. Missing key means not analyzed.</summary>
    public IReadOnlyDictionary<string, Services.VideoDashboardStatus> VideoStatuses { get; init; } =
        new Dictionary<string, Services.VideoDashboardStatus>();

    /// <summary>Pass back as ?pageToken= to fetch the next page of videos; null on the last page.</summary>
    public string? NextPageToken { get; init; }

    /// <summary>Pass back as ?pageToken= to fetch the previous page of videos; null on the first page.</summary>
    public string? PrevPageToken { get; init; }

    /// <summary>Set when connected, but loading the channel or its videos still failed.</summary>
    public string? LoadError { get; init; }
}
