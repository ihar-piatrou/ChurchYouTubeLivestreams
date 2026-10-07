using ChurchYouTubeAssistant.Models;

namespace ChurchYouTubeAssistant.Services;

/// <summary>
/// Owns the Google OAuth 2.0 authorization-code flow and the lifecycle of the resulting tokens.
/// </summary>
/// <remarks>
/// Note what is NOT here: HTTP concerns (redirects, status codes) belong to the controllers, and
/// YouTube Data API calls belong to <see cref="IYouTubeReadService"/>. Callers of this interface
/// never see an access or refresh token except <see cref="GetAccessTokenAsync"/>, which exists
/// solely so the YouTube service can authenticate its requests.
/// </remarks>
public interface IGoogleOAuthService
{
    /// <summary>
    /// Builds the Google authorization URL to redirect the admin's browser to, issuing and
    /// remembering a fresh CSRF state value as a side effect.
    /// </summary>
    string BuildAuthorizationUrl();

    /// <summary>
    /// Validates the callback, exchanges the authorization code for tokens and stores them.
    /// </summary>
    /// <exception cref="Exceptions.OAuthFlowException">
    /// State validation, the authorization code or the token exchange failed.
    /// </exception>
    Task<YouTubeConnectionStatusDto> CompleteAuthorizationAsync(
        string? code,
        string? state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes a state value for an authorization that will not complete (for example the user
    /// denied consent), so it cannot be replayed.
    /// </summary>
    void AbandonAuthorization(string? state);

    /// <summary>
    /// Returns a usable access token, refreshing it first if it is expired or about to expire.
    /// </summary>
    /// <param name="forceRefresh">
    /// Refresh even if the stored token still looks valid. Used after an unexpected 401, when
    /// Google disagrees with our expiry bookkeeping.
    /// </param>
    /// <exception cref="Exceptions.YouTubeNotConnectedException">
    /// No tokens are stored, or the access token has expired and there is no refresh token.
    /// </exception>
    /// <exception cref="Exceptions.OAuthFlowException">The refresh attempt was rejected by Google.</exception>
    Task<string> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>Reports whether YouTube is connected, without exposing any token material.</summary>
    Task<YouTubeConnectionStatusDto> GetConnectionStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Revokes the authorization at Google (best effort) and forgets the stored tokens.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
