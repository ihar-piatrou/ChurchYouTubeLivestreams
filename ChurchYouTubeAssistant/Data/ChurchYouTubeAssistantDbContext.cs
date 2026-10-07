using System.Text.Json;
using ChurchYouTubeAssistant.Models;
using ChurchYouTubeAssistant.Models.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ChurchYouTubeAssistant.Data;

/// <summary>
/// Holds the per-video cache (title/description/transcript) and the AI video-analysis history.
/// </summary>
public sealed class ChurchYouTubeAssistantDbContext(DbContextOptions<ChurchYouTubeAssistantDbContext> options)
    : DbContext(options)
{
    public DbSet<VideoRecord> Videos => Set<VideoRecord>();
    public DbSet<VideoAnalysis> VideoAnalyses => Set<VideoAnalysis>();

    /// <summary>Stores a plain List&lt;string&gt; as a single JSON array column, e.g. keywords/themes.</summary>
    private static readonly ValueConverter<List<string>, string> StringListConverter = new(
        list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
        json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>());

    /// <summary>
    /// Tells EF how to tell whether a converted List&lt;string&gt; changed (by content, not just
    /// reference), so change tracking and SaveChanges work correctly even if a list is ever
    /// mutated in place rather than replaced outright.
    /// </summary>
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
        list => (list ?? new List<string>()).Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        list => new List<string>(list ?? new List<string>()));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VideoRecord>(video =>
        {
            video.ToTable("Videos");
            video.HasKey(v => v.VideoId);

            // YouTube video ids are 11 characters today, but a short fixed length would break if
            // that ever changes; 32 leaves headroom without being unbounded.
            video.Property(v => v.VideoId).HasMaxLength(32);

            video.Property(v => v.Title).HasMaxLength(500);
            video.Property(v => v.ThumbnailUrl).HasMaxLength(500);
            video.Property(v => v.TranscriptLanguage).HasMaxLength(20);
            video.Property(v => v.TranscriptFormat).HasMaxLength(10);
            video.Property(v => v.TranscriptCaptionTrackId).HasMaxLength(100);

            // Description and transcript content are unbounded text (nvarchar(max) by convention
            // once no MaxLength is set on a SQL Server string column).
        });

        modelBuilder.Entity<VideoAnalysis>(analysis =>
        {
            analysis.ToTable("VideoAnalyses");
            analysis.HasKey(a => a.Id);

            // Most-recent-first history lookups (GET /analyses) and the publish workflow both
            // filter by video, so this is the one index worth adding now.
            analysis.HasIndex(a => new { a.VideoId, a.CreatedAtUtc });

            analysis.Property(a => a.VideoId).HasMaxLength(32);
            analysis.Property(a => a.Model).HasMaxLength(100);
            analysis.Property(a => a.PromptVersion).HasMaxLength(20);
            analysis.Property(a => a.Language).HasMaxLength(20);
            analysis.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

            // Simple string-list fields: one JSON array per column via a value converter.
            analysis.Property(a => a.AlternativeTitles).HasConversion(StringListConverter, StringListComparer);
            analysis.Property(a => a.KeyThemes).HasConversion(StringListConverter, StringListComparer);
            analysis.Property(a => a.Keywords).HasConversion(StringListConverter, StringListComparer);
            analysis.Property(a => a.Notes).HasConversion(StringListConverter, StringListComparer);

            // Structured nested content: EF Core's native JSON column mapping for owned types, so
            // BibleReferenceInfo/ChapterInfo/ThumbnailConcept/ShortCandidate stay strongly typed in
            // C# while stored as one JSON document per column, rather than needing child tables.
            analysis.OwnsMany(a => a.BibleReferences, b => b.ToJson());
            analysis.OwnsMany(a => a.Chapters, c => c.ToJson());
            analysis.OwnsOne(a => a.Thumbnail, t => t.ToJson());
            analysis.OwnsMany(a => a.Shorts, s => s.ToJson());
        });
    }
}
