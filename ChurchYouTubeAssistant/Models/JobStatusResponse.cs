namespace ChurchYouTubeAssistant.Models;

/// <summary>JSON shape returned by the Analysis page's job-status polling endpoint.</summary>
public sealed record JobStatusResponse(string State, string? RedirectUrl);
