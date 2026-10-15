using ChurchYouTubeAssistant.Data;
using ChurchYouTubeAssistant.Models;
using ChurchYouTubeAssistant.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace ChurchYouTubeAssistant.Services;

/// <inheritdoc cref="IAnalyticsService"/>
public sealed class AnalyticsService(ChurchYouTubeAssistantDbContext db) : IAnalyticsService
{
    private const int TopBookCount = 20;
    private const int RarelyReferencedMaxCount = 2;
    private const int UnparsedSampleCount = 20;

    public async Task<AnalyticsViewModel> GetAnalyticsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        // Two passes: first pick just the id of the latest qualifying analysis per video (a cheap
        // scalar projection, same GroupBy+OrderByDescending+First() shape GetDashboardStatusesAsync
        // already uses), then load only those full rows. Loading every past (superseded) analysis in
        // full would drag along every re-analysis attempt's RawOpenAiResponse and generated thumbnail
        // image bytes for no benefit - this keeps that cost to one row per video.
        var latestIds = await db.VideoAnalyses
            .Where(a => a.Status == AnalysisStatus.Succeeded || a.Status == AnalysisStatus.PartiallyValid)
            .GroupBy(a => a.VideoId)
            .Select(g => g.OrderByDescending(a => a.CreatedAtUtc).First().Id)
            .ToListAsync(cancellationToken);

        var latestPerVideo = await db.VideoAnalyses
            .Where(a => latestIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

        var videoIds = latestPerVideo.Select(a => a.VideoId).ToList();
        var videosById = await db.Videos
            .Where(v => videoIds.Contains(v.VideoId))
            .ToDictionaryAsync(v => v.VideoId, cancellationToken);

        // The sermon's real-world date is when it aired on YouTube, not when we happened to run the
        // AI analysis - fall back to the analysis date only if a video's cache row is somehow missing
        // (analyzing a video always caches it first, so this should not normally happen).
        var sermons = latestPerVideo
            .Select(a => new
            {
                Analysis = a,
                SermonDate = DateOnly.FromDateTime(
                    (videosById.TryGetValue(a.VideoId, out var video) ? video.PublishedAt : null)
                        ?.UtcDateTime ?? a.CreatedAtUtc.UtcDateTime)
            })
            .Where(s => s.SermonDate >= from && s.SermonDate <= to)
            .OrderBy(s => s.SermonDate)
            .ToList();

        // --- Bible references ---
        var primaryCounts = new Dictionary<string, int>();
        var totalCounts = new Dictionary<string, int>();
        var unparsedSamples = new List<string>();
        var unparsedCount = 0;

        foreach (var reference in sermons.SelectMany(s => s.Analysis.BibleReferences))
        {
            var book = BibleBookCatalog.TryMatch(reference.Reference);
            if (book is null)
            {
                unparsedCount++;
                if (unparsedSamples.Count < UnparsedSampleCount)
                {
                    unparsedSamples.Add(reference.Reference);
                }
                continue;
            }

            totalCounts[book.CanonicalName] = totalCounts.GetValueOrDefault(book.CanonicalName) + 1;
            if (reference.IsPrimary)
            {
                primaryCounts[book.CanonicalName] = primaryCounts.GetValueOrDefault(book.CanonicalName) + 1;
            }
        }

        var bibleCoverage = BibleBookCatalog.AllBooks
            .Select(b => new BibleCoverageRow(
                b.CanonicalName,
                b.Testament,
                primaryCounts.GetValueOrDefault(b.CanonicalName),
                totalCounts.GetValueOrDefault(b.CanonicalName)))
            .ToList();

        var neverReferenced = bibleCoverage.Where(r => r.TotalCount == 0).Select(r => r.BookName).ToList();
        var rarelyReferenced = bibleCoverage
            .Where(r => r.TotalCount is > 0 and <= RarelyReferencedMaxCount)
            .Select(r => r.BookName)
            .ToList();

        // --- Theology categories ---
        var uncategorizedCount = sermons.Count(s => s.Analysis.PrimaryCategory is null);

        var categoryFrequency = sermons
            .Where(s => s.Analysis.PrimaryCategory is not null)
            .GroupBy(s => s.Analysis.PrimaryCategory!.Value)
            .Select(g => new NameCount(TheologyCategoryCatalog.EnglishNameOf(g.Key), g.Count()))
            // "Uncategorized" rides in the same chart as a real bar, not just the separate
            // text+button above it, so its size is visible relative to the named categories.
            .Concat(uncategorizedCount > 0 ? [new NameCount("Uncategorized", uncategorizedCount)] : [])
            .OrderByDescending(nc => nc.Count)
            .ToList();

        var topBooks = bibleCoverage
            .Where(r => r.PrimaryCount > 0)
            .OrderByDescending(r => r.PrimaryCount)
            .Take(TopBookCount)
            .Select(r => new NameCount(r.BookName, r.PrimaryCount))
            .ToList();

        return new AnalyticsViewModel
        {
            From = from,
            To = to,
            SermonCount = sermons.Count,
            UnparsedReferenceCount = unparsedCount,
            UnparsedReferenceSamples = unparsedSamples,
            TopBooksByPrimaryReferences = topBooks,
            BibleCoverage = bibleCoverage,
            NeverReferencedBooks = neverReferenced,
            RarelyReferencedBooks = rarelyReferenced,
            CategoryFrequency = categoryFrequency,
            UncategorizedCount = uncategorizedCount
        };
    }
}
