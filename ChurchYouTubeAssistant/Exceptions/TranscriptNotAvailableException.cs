namespace ChurchYouTubeAssistant.Exceptions;

/// <summary>
/// The video has no caption tracks at all, or none in the requested language.
/// Surfaced as 404 Not Found: the video and channel are fine, this particular transcript isn't.
/// </summary>
public sealed class TranscriptNotAvailableException(string message) : Exception(message);
