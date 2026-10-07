using ChurchYouTubeAssistant.Auth;
using ChurchYouTubeAssistant.Configuration;
using ChurchYouTubeAssistant.Exceptions;
using ChurchYouTubeAssistant.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Extensions.Options;

namespace ChurchYouTubeAssistant.Services;

/// <summary>
/// Google OAuth 2.0 authorization-code flow for a server-side web application, implemented on top
/// of <see cref="GoogleAuthorizationCodeFlow"/> from Google.Apis.Auth.
/// </summary>
/// <remarks>
/// The Google library is used for the protocol (building the authorization URL, the token endpoint
/// requests, parsing and error handling) because reimplementing that by hand is exactly where
/// OAuth bugs live. Storage and CSRF state are ours, via <see cref="IYouTubeTokenStore"/> and
/// <see cref="IOAuthStateStore"/>.
/// <para>
/// Registered as a singleton: the flow owns an <see cref="System.Net.Http.HttpClient"/> and is
/// built once from configuration.
/// </para>
/// </remarks>
public sealed class GoogleOAuthService : IGoogleOAuthService, IDisposable
{
    /// <summary>
    /// Key the Google flow uses for its (no-op) data store. Stage 1 connects a single church
    /// channel; this becomes a real per-channel identifier when multi-channel support arrives.
    /// </summary>
    private const string FlowUserId = "church-youtube-account";

    private readonly GoogleOAuthOptions _options;
    private readonly IYouTubeTokenStore _tokenStore;
    private readonly IOAuthStateStore _stateStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GoogleOAuthService> _logger;
    private readonly GoogleAuthorizationCodeFlow _flow;

    /// <summary>Serialises refresh attempts so concurrent requests cannot stampede the token endpoint.</summary>
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public GoogleOAuthService(
        IOptions<GoogleOAuthOptions> options,
        IYouTubeTokenStore tokenStore,
        IOAuthStateStore stateStore,
        TimeProvider timeProvider,
        ILogger<GoogleOAuthService> logger)
    {
        _options = options.Value;
        _tokenStore = tokenStore;
        _stateStore = stateStore;
        _timeProvider = timeProvider;
        _logger = logger;

        _flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret
            },
            Scopes = GoogleOAuthOptions.Scopes,

            // We persist tokens ourselves; see NoOpGoogleDataStore for why the default is refused.
            DataStore = new NoOpGoogleDataStore()
        });
    }

    public string BuildAuthorizationUrl()
    {
        var request = (GoogleAuthorizationCodeRequestUrl)_flow.CreateAuthorizationCodeRequest(_options.RedirectUri);

        // Single-use CSRF token, verified in CompleteAuthorizationAsync.
        request.State = _stateStore.CreateState();

        // access_type=offline is what makes Google willing to issue a refresh token at all.
        request.AccessType = "offline";

        // prompt=consent forces the consent screen every time, which is what guarantees a refresh
        // token. Google only returns a refresh token on an authorization where consent was actually
        // granted; a returning user who already approved these scopes is otherwise sent straight
        // through and the response contains an access token only. Connecting is a rare, deliberate
        // admin action, so paying one extra screen to be certain unattended refresh will work is
        // the right trade. (The alternative is prompt=select_account plus a stored refresh token
        // that we must never lose.)
        request.Prompt = "consent";

        var url = request.Build().ToString();
        _logger.LogInformation(
            "Starting Google OAuth authorization for scopes [{Scopes}], redirect URI {RedirectUri}.",
            string.Join(", ", GoogleOAuthOptions.Scopes),
            _options.RedirectUri);

        return url;
    }

    public async Task<YouTubeConnectionStatusDto> CompleteAuthorizationAsync(
        string? code,
        string? state,
        CancellationToken cancellationToken = default)
    {
        // State first: an unverified callback must not reach the token endpoint.
        if (!_stateStore.TryConsumeState(state))
        {
            throw new OAuthFlowException(
                OAuthFlowError.InvalidState,
                "The OAuth state value was missing, expired or already used. Start again at /youtube/connect.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new OAuthFlowException(
                OAuthFlowError.MissingAuthorizationCode,
                "Google did not return an authorization code. Start again at /youtube/connect.");
        }

        TokenResponse response;
        try
        {
            response = await _flow.ExchangeCodeForTokenAsync(
                FlowUserId, code, _options.RedirectUri, cancellationToken);
        }
        catch (TokenResponseException ex)
        {
            // Error/description are Google's own diagnostics and contain no credentials.
            _logger.LogError(
                ex,
                "Authorization code exchange failed: {Error} ({ErrorDescription}).",
                ex.Error?.Error,
                ex.Error?.ErrorDescription);

            throw new OAuthFlowException(
                OAuthFlowError.TokenExchangeFailed,
                BuildTokenExchangeFailureMessage(ex),
                ex);
        }

        var tokens = ToTokenInfo(response, previous: null);

        if (!GrantedScopesInclude(tokens.GrantedScopes, GoogleOAuthOptions.Scopes))
        {
            _logger.LogError(
                "Authorization completed without the required scopes. Granted: [{GrantedScopes}].",
                tokens.GrantedScopes);

            throw new OAuthFlowException(
                OAuthFlowError.RequiredScopeNotGranted,
                $"The required scope(s) [{string.Join(", ", GoogleOAuthOptions.Scopes)}] were not granted. " +
                "Authorize again and leave the YouTube permission checked.");
        }

        await _tokenStore.SaveAsync(tokens, cancellationToken);

        if (!tokens.HasRefreshToken)
        {
            // Not fatal now, but it means this connection cannot survive the access token expiring.
            _logger.LogWarning(
                "Google returned no refresh token. Unattended processing will stop working once the " +
                "access token expires at {ExpiresAtUtc:O}. Reconnect, or revoke the app's access at " +
                "https://myaccount.google.com/permissions and reconnect, to obtain one.",
                tokens.ExpiresAtUtc);
        }

        _logger.LogInformation(
            "YouTube connected. Access token expires {ExpiresAtUtc:O}; refresh token present: {HasRefreshToken}.",
            tokens.ExpiresAtUtc,
            tokens.HasRefreshToken);

        return ToStatus(tokens);
    }

    public void AbandonAuthorization(string? state) => _stateStore.TryConsumeState(state);

    public async Task<string> GetAccessTokenAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var tokens = await _tokenStore.GetAsync(cancellationToken)
            ?? throw new YouTubeNotConnectedException(
                "YouTube has not been connected yet. Visit GET /youtube/connect and authorize access first.");

        var now = _timeProvider.GetUtcNow();
        if (!forceRefresh && !tokens.NeedsRefresh(now, GoogleOAuthOptions.TokenRefreshSkew))
        {
            return tokens.AccessToken;
        }

        if (!tokens.HasRefreshToken)
        {
            if (tokens.IsExpired(now))
            {
                throw new YouTubeNotConnectedException(
                    "The stored access token has expired and no refresh token is available. " +
                    "Visit GET /youtube/connect to authorize again.");
            }

            // Inside the skew window but still technically valid, and we have no way to renew it.
            _logger.LogWarning(
                "Access token expires at {ExpiresAtUtc:O} and cannot be refreshed; using it as-is.",
                tokens.ExpiresAtUtc);
            return tokens.AccessToken;
        }

        return await RefreshAccessTokenAsync(tokens, forceRefresh, now, cancellationToken);
    }

    public async Task<YouTubeConnectionStatusDto> GetConnectionStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var tokens = await _tokenStore.GetAsync(cancellationToken);
        return tokens is null
            ? new YouTubeConnectionStatusDto
            {
                Connected = false,
                CanRefreshUnattended = false,
                Message = "YouTube is not connected. Visit GET /youtube/connect to authorize access."
            }
            : ToStatus(tokens);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await _tokenStore.GetAsync(cancellationToken);

        if (tokens?.RefreshToken is { Length: > 0 } refreshToken)
        {
            try
            {
                // Best effort: tell Google to drop the grant, so a stale copy of the refresh token
                // is worthless even if it leaked from storage.
                await _flow.RevokeTokenAsync(FlowUserId, refreshToken, cancellationToken);
                _logger.LogInformation("Revoked the YouTube authorization at Google.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not revoke the authorization at Google; clearing locally anyway.");
            }
        }

        await _tokenStore.ClearAsync(cancellationToken);
    }

    public void Dispose()
    {
        _flow.Dispose();
        _refreshLock.Dispose();
    }

    private async Task<string> RefreshAccessTokenAsync(
        OAuthTokenInfo tokens,
        bool forceRefresh,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Another request may have refreshed while we waited for the lock, in which case the
            // freshly read tokens no longer need refreshing and we reuse them.
            var current = await _tokenStore.GetAsync(cancellationToken) ?? tokens;
            if (!forceRefresh && !current.NeedsRefresh(now, GoogleOAuthOptions.TokenRefreshSkew))
            {
                return current.AccessToken;
            }

            var refreshToken = current.RefreshToken ?? tokens.RefreshToken!;

            TokenResponse response;
            try
            {
                response = await _flow.RefreshTokenAsync(FlowUserId, refreshToken, cancellationToken);
            }
            catch (TokenResponseException ex)
            {
                _logger.LogError(
                    ex,
                    "Refreshing the access token failed: {Error} ({ErrorDescription}).",
                    ex.Error?.Error,
                    ex.Error?.ErrorDescription);

                // invalid_grant means the grant is gone for good: revoked by the user, expired
                // because the app is still in Testing mode (refresh tokens last 7 days there), or
                // the password changed. A human has to reconnect.
                var message = string.Equals(ex.Error?.Error, "invalid_grant", StringComparison.OrdinalIgnoreCase)
                    ? "The stored refresh token is no longer valid (it was revoked, or it expired because " +
                      "the Google Cloud app is still in Testing mode, where refresh tokens last 7 days). " +
                      "Visit GET /youtube/connect to authorize again."
                    : "Could not refresh the YouTube access token. Visit GET /youtube/connect to authorize again.";

                throw new OAuthFlowException(OAuthFlowError.RefreshFailed, message, ex);
            }

            var refreshed = ToTokenInfo(response, previous: current);
            await _tokenStore.SaveAsync(refreshed, cancellationToken);

            _logger.LogInformation(
                "Refreshed the YouTube access token; it now expires {ExpiresAtUtc:O}.",
                refreshed.ExpiresAtUtc);

            return refreshed.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Maps Google's token response onto our storage model.</summary>
    private OAuthTokenInfo ToTokenInfo(TokenResponse response, OAuthTokenInfo? previous)
    {
        if (string.IsNullOrWhiteSpace(response.AccessToken))
        {
            throw new OAuthFlowException(
                OAuthFlowError.TokenExchangeFailed,
                "Google's token response contained no access token.");
        }

        var issuedUtc = response.IssuedUtc == default
            ? _timeProvider.GetUtcNow()
            : new DateTimeOffset(DateTime.SpecifyKind(response.IssuedUtc, DateTimeKind.Utc));

        // Google omits expires_in in rare cases; one hour is its documented default lifetime.
        var lifetime = TimeSpan.FromSeconds(response.ExpiresInSeconds ?? 3600);

        return new OAuthTokenInfo
        {
            AccessToken = response.AccessToken,

            // A refresh response has no refresh_token of its own; keep the one we already hold.
            RefreshToken = response.RefreshToken ?? previous?.RefreshToken,
            ExpiresAtUtc = issuedUtc + lifetime,
            GrantedScopes = response.Scope ?? previous?.GrantedScopes,
            TokenType = response.TokenType ?? previous?.TokenType,
            ObtainedAtUtc = issuedUtc
        };
    }

    private YouTubeConnectionStatusDto ToStatus(OAuthTokenInfo tokens) => new()
    {
        Connected = true,
        CanRefreshUnattended = tokens.HasRefreshToken,
        AccessTokenExpiresAtUtc = tokens.ExpiresAtUtc,
        ConnectedAtUtc = tokens.ObtainedAtUtc,
        GrantedScopes = tokens.GrantedScopes,
        Message = tokens.HasRefreshToken
            ? null
            : "Connected, but Google did not return a refresh token, so this connection cannot be " +
              "renewed without an admin signing in again."
    };

    private static bool GrantedScopesInclude(string? grantedScopes, IEnumerable<string> required)
    {
        if (string.IsNullOrWhiteSpace(grantedScopes))
        {
            // Google normally echoes the granted scopes; if it did not we cannot prove a problem.
            return true;
        }

        var granted = grantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return required.All(scope => granted.Contains(scope, StringComparer.Ordinal));
    }

    private static string BuildTokenExchangeFailureMessage(TokenResponseException ex) =>
        string.Equals(ex.Error?.Error, "redirect_uri_mismatch", StringComparison.OrdinalIgnoreCase)
            ? "Google rejected the token exchange with redirect_uri_mismatch. The configured " +
              "Google:RedirectUri must match an authorised redirect URI on the OAuth client exactly."
            : $"Could not exchange the authorization code for tokens ({ex.Error?.Error ?? "unknown error"}). " +
              "Start again at /youtube/connect.";
}
