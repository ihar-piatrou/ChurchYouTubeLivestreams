namespace ChurchYouTubeAssistant.Models;

/// <summary>Non-sensitive view of the stored authorization, safe to return and to log.</summary>
public sealed record YouTubeConnectionStatusDto
{
    public required bool Connected { get; init; }

    /// <summary>
    /// False means unattended background processing is impossible: the next access-token expiry
    /// will require a human to re-authorize.
    /// </summary>
    public required bool CanRefreshUnattended { get; init; }

    public DateTimeOffset? AccessTokenExpiresAtUtc { get; init; }
    public DateTimeOffset? ConnectedAtUtc { get; init; }
    public string? GrantedScopes { get; init; }

    /// <summary>What the caller should do next, e.g. where to go to connect.</summary>
    public string? Message { get; init; }
}
