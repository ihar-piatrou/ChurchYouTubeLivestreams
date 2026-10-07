namespace ChurchYouTubeAssistant.Exceptions;

/// <summary>Classification of OAuth failures, used to pick an HTTP status and a message.</summary>
public enum OAuthFlowError
{
    /// <summary>Google reported an error instead of a code, e.g. the user clicked "Cancel".</summary>
    ConsentDenied,

    /// <summary>The state parameter was missing, unknown, replayed or expired. Treat as an attack.</summary>
    InvalidState,

    /// <summary>Google redirected back without an authorization code.</summary>
    MissingAuthorizationCode,

    /// <summary>Google refused to exchange the authorization code for tokens.</summary>
    TokenExchangeFailed,

    /// <summary>Google refused the refresh token; it was revoked, expired or the client changed.</summary>
    RefreshFailed,

    /// <summary>The user authorized, but withheld a scope the application cannot work without.</summary>
    RequiredScopeNotGranted
}

/// <summary>A failure in the Google OAuth 2.0 authorization-code flow.</summary>
public sealed class OAuthFlowException : Exception
{
    public OAuthFlowException(OAuthFlowError error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public OAuthFlowError Error { get; }
}
