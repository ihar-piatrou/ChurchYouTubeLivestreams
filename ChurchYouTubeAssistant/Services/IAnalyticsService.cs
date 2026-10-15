using ChurchYouTubeAssistant.Models.ViewModels;

namespace ChurchYouTubeAssistant.Services;

/// <summary>Aggregates sermon-analysis history (topics, Bible references, keywords) for the analytics dashboard.</summary>
public interface IAnalyticsService
{
    Task<AnalyticsViewModel> GetAnalyticsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
