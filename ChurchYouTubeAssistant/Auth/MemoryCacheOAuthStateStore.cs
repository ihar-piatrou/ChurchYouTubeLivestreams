using System.Security.Cryptography;
using ChurchYouTubeAssistant.Auth;
using Microsoft.Extensions.Caching.Memory;

namespace ChurchYouTubeAssistant.Auth;

/// <summary>
/// Server-side state store backed by <see cref="IMemoryCache"/>. State never leaves the server,
/// so nothing a caller sends us can be trusted into existence.
/// </summary>
/// <remarks>
/// In-process storage means pending authorizations do not survive a restart and do not work across
/// multiple instances. That is fine while connecting is a rare, interactive admin action, but when
/// this is scaled out on AWS/Azure the state store must move to a distributed cache
/// (Redis / ElastiCache) or to a signed, short-lived, encrypted state value. Only this class
/// changes; callers depend on <see cref="IOAuthStateStore"/>.
/// </remarks>
public sealed class MemoryCacheOAuthStateStore(IMemoryCache cache, ILogger<MemoryCacheOAuthStateStore> logger)
    : IOAuthStateStore
{
    private const string KeyPrefix = "oauth-state:";

    /// <summary>How long the admin has to complete the Google consent screen.</summary>
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    public string CreateState()
    {
        // 256 bits from a CSPRNG, URL-safe so it survives the query string unescaped.
        var state = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        cache.Set(KeyPrefix + state, true, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = StateLifetime
        });

        logger.LogDebug("Issued OAuth state value, valid for {Lifetime}.", StateLifetime);
        return state;
    }

    public bool TryConsumeState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            logger.LogWarning("OAuth callback had no state parameter; rejecting.");
            return false;
        }

        var key = KeyPrefix + state;
        if (!cache.TryGetValue(key, out _))
        {
            // Unknown, expired, or already consumed. We never log the value itself.
            logger.LogWarning("OAuth callback presented an unrecognised or expired state value; rejecting.");
            return false;
        }

        // Single use: remove before returning so a replayed callback fails.
        cache.Remove(key);
        return true;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
