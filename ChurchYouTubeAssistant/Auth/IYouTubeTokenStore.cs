using ChurchYouTubeAssistant.Models;

namespace ChurchYouTubeAssistant.Auth;

/// <summary>
/// Persistence boundary for the church channel's OAuth tokens.
/// </summary>
/// <remarks>
/// Nothing above this interface knows how or where tokens are kept, so the development
/// implementation can be replaced with encrypted database storage (or AWS Secrets Manager /
/// Azure Key Vault) without touching the OAuth or YouTube services.
/// <para>
/// Stage 1 models exactly one connected channel. When the app has to serve several channels the
/// natural change is to add an account/channel key to these methods; keeping that out of the
/// signature now avoids a parameter that every caller would have to pass a constant into.
/// </para>
/// </remarks>
public interface IYouTubeTokenStore
{
    /// <summary>Returns the stored tokens, or null when YouTube has never been connected.</summary>
    ValueTask<OAuthTokenInfo?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores tokens, replacing anything already held.</summary>
    ValueTask SaveAsync(OAuthTokenInfo tokens, CancellationToken cancellationToken = default);

    /// <summary>Forgets the stored tokens. The admin must re-authorize afterwards.</summary>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}
