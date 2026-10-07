using ChurchYouTubeAssistant.Models;

namespace ChurchYouTubeAssistant.Auth;

/// <summary>
/// DEVELOPMENT ONLY. Holds the tokens in process memory.
/// </summary>
/// <remarks>
/// NOT SUITABLE FOR PRODUCTION:
/// <list type="bullet">
/// <item>tokens are lost on every restart, so the admin must re-authorize each time;</item>
/// <item>nothing is shared between instances, so a scaled-out deployment would behave randomly;</item>
/// <item>a long-lived refresh token is a credential to the church channel and belongs in
/// encrypted storage with an audit trail, not in a process variable.</item>
/// </list>
/// Replace with an implementation backed by a database column encrypted at rest (or a secret
/// manager) before this runs anywhere other than a developer machine. Only the DI registration
/// in Program.cs changes.
/// </remarks>
public sealed class DevelopmentYouTubeTokenStore(ILogger<DevelopmentYouTubeTokenStore> logger)
    : IYouTubeTokenStore
{
    private readonly object _gate = new();
    private OAuthTokenInfo? _tokens;

    public ValueTask<OAuthTokenInfo?> GetAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_tokens);
        }
    }

    public ValueTask SaveAsync(OAuthTokenInfo tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        lock (_gate)
        {
            _tokens = tokens;
        }

        logger.LogWarning(
            "YouTube tokens stored in the DEVELOPMENT in-memory token store. They will be lost when " +
            "the application stops and are not suitable for production use.");

        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _tokens = null;
        }

        logger.LogInformation("Stored YouTube tokens cleared.");
        return ValueTask.CompletedTask;
    }
}
