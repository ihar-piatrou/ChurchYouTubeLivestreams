namespace ChurchYouTubeAssistant.Exceptions;

/// <summary>
/// The YouTube Data API was reachable but the call failed (quota exceeded, forbidden, bad
/// response, missing expected data). Surfaced as 502 Bad Gateway: our request was fine, the
/// upstream dependency was not.
/// </summary>
public sealed class YouTubeIntegrationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
