namespace ChurchYouTubeAssistant.Exceptions;

/// <summary>
/// No usable authorization is stored, so no YouTube call can be made.
/// Surfaced to callers as 409 Conflict with instructions to connect.
/// </summary>
public sealed class YouTubeNotConnectedException(string message) : Exception(message);
