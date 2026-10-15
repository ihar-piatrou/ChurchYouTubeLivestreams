using ChurchYouTubeAssistant.Services;

namespace ChurchYouTubeAssistant.Models.ViewModels;

/// <summary>View model for the sermon-analytics page (Home/Analytics).</summary>
public sealed class AnalyticsViewModel
{
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }

    /// <summary>Distinct sermons (one per video, latest succeeded/partially-valid analysis) in range.</summary>
    public required int SermonCount { get; init; }

    /// <summary>References that could not be matched to one of the 66 canonical books - see <see cref="BibleBookCatalog"/>.</summary>
    public required int UnparsedReferenceCount { get; init; }
    public required IReadOnlyList<string> UnparsedReferenceSamples { get; init; }

    /// <summary>Most-referenced books, primary passages only, sorted descending, capped to the top 20.</summary>
    public required IReadOnlyList<NameCount> TopBooksByPrimaryReferences { get; init; }

    /// <summary>All 66 canonical books with their reference counts, in canonical (Genesis-Revelation) order.</summary>
    public required IReadOnlyList<BibleCoverageRow> BibleCoverage { get; init; }
    public required IReadOnlyList<string> NeverReferencedBooks { get; init; }
    public required IReadOnlyList<string> RarelyReferencedBooks { get; init; }

    /// <summary>Sermon count per primary theology category, descending, within the date range.</summary>
    public required IReadOnlyList<NameCount> CategoryFrequency { get; init; }

    /// <summary>Sermons in range whose latest analysis has no PrimaryCategory yet - see HomeController.CategorizeUncategorized.</summary>
    public required int UncategorizedCount { get; init; }
}

public sealed record NameCount(string Name, int Count);

public sealed record BibleCoverageRow(string BookName, BibleTestament Testament, int PrimaryCount, int TotalCount);
