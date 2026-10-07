using GoogleYouTubeService = Google.Apis.YouTube.v3.YouTubeService;

namespace ChurchYouTubeAssistant.Configuration;

/// <summary>
/// Strongly typed Google OAuth configuration, bound from the "Google" configuration section.
/// </summary>
/// <remarks>
/// <see cref="ClientId"/> and <see cref="ClientSecret"/> must come from a secret source
/// (.NET User Secrets locally, environment variables / Key Vault / Secrets Manager when hosted).
/// They must never be committed to appsettings.json.
/// </remarks>
public sealed class GoogleOAuthOptions
{
    public const string SectionName = "Google";

    /// <summary>OAuth 2.0 client id of the "Web application" client created in Google Cloud.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>OAuth 2.0 client secret. Supplied via User Secrets / environment, never source control.</summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// Must match one of the "Authorised redirect URIs" registered on the Google OAuth client
    /// byte for byte, otherwise Google returns redirect_uri_mismatch.
    /// </summary>
    public string RedirectUri { get; init; } = string.Empty;

    /// <summary>
    /// Scopes requested during authorization.
    /// </summary>
    /// <remarks>
    /// <see cref="GoogleYouTubeService.Scope.YoutubeForceSsl"/> is here solely because
    /// <c>captions.download</c> requires it - Google gates caption *content* download behind the
    /// same scope as full channel management, even though downloading a transcript is conceptually
    /// read-only. This is a real widening of what the stored refresh token is *capable of*: unlike
    /// youtube.readonly, force-ssl also permits videos.update, thumbnails.set, playlist edits, etc.
    /// This app does not implement or call any of those, and <see cref="Services.IYouTubeReadService"/>
    /// stays read-only by contract, but if the stored token ever leaked, it would carry that wider
    /// power. Treat token storage (see IYouTubeTokenStore) as correspondingly more sensitive now.
    /// Write scopes proper (youtube.upload) are only added when the approval workflow and
    /// videos.update / thumbnails.set support land.
    /// </remarks>
    public static readonly string[] Scopes =
    [
        GoogleYouTubeService.Scope.YoutubeReadonly,
        GoogleYouTubeService.Scope.YoutubeForceSsl
    ];

    /// <summary>
    /// Access tokens are refreshed this far ahead of their real expiry, to absorb clock skew
    /// and the latency of the call we are about to make.
    /// </summary>
    public static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromMinutes(5);
}
