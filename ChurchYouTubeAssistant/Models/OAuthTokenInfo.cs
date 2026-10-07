namespace ChurchYouTubeAssistant.Models;

/// <summary>
/// The stored result of a Google OAuth authorization. Internal model: it carries secrets and
/// must never be returned from an API endpoint or written to a log.
/// </summary>
public sealed record OAuthTokenInfo
{
    public required string AccessToken { get; init; }

    /// <summary>
    /// Long-lived token used for unattended refresh. Null when Google did not return one
    /// (it only issues a refresh token when access_type=offline AND the user is actually
    /// shown the consent screen).
    /// </summary>
    public string? RefreshToken { get; init; }

    /// <summary>Absolute UTC expiry of <see cref="AccessToken"/>.</summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }

    /// <summary>Space-delimited scopes actually granted by the user.</summary>
    public string? GrantedScopes { get; init; }

    public string? TokenType { get; init; }

    public required DateTimeOffset ObtainedAtUtc { get; init; }

    public bool HasRefreshToken => !string.IsNullOrWhiteSpace(RefreshToken);

    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;

    /// <summary>True when the access token is expired or close enough to expiry to be unsafe to use.</summary>
    public bool NeedsRefresh(DateTimeOffset nowUtc, TimeSpan skew) => nowUtc + skew >= ExpiresAtUtc;

    /// <summary>
    /// Redacted on purpose: the compiler-generated record ToString() would print the access and
    /// refresh tokens, which would leak them into any log line or exception message.
    /// </summary>
    public override string ToString() =>
        $"OAuthTokenInfo {{ ExpiresAtUtc = {ExpiresAtUtc:O}, HasRefreshToken = {HasRefreshToken}, " +
        $"GrantedScopes = {GrantedScopes}, AccessToken = [redacted], RefreshToken = [redacted] }}";
}
