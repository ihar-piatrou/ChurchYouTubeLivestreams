namespace ChurchYouTubeAssistant.Auth;

/// <summary>
/// Issues and verifies the OAuth <c>state</c> parameter, which is what stops a third party from
/// feeding us an authorization code of their own (CSRF against the callback endpoint).
/// </summary>
public interface IOAuthStateStore
{
    /// <summary>Creates and remembers a single-use, cryptographically random state value.</summary>
    string CreateState();

    /// <summary>
    /// Verifies the state returned by Google and consumes it so it cannot be replayed.
    /// Returns false for missing, unknown, expired or already-used values.
    /// </summary>
    bool TryConsumeState(string? state);
}
