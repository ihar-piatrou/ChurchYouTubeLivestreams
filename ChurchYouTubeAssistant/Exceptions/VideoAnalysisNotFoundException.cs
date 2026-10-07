namespace ChurchYouTubeAssistant.Exceptions;

/// <summary>No analysis with the given id exists for the given video. Surfaced as 404 Not Found.</summary>
public sealed class VideoAnalysisNotFoundException(string message) : Exception(message);
