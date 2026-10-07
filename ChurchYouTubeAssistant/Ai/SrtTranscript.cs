using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ChurchYouTubeAssistant.Ai;

/// <summary>One parsed SRT cue: a time range and its text.</summary>
public sealed record SrtCue(int Index, TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// Minimal SRT parser used to split long transcripts into timestamp-preserving chunks for the
/// multi-stage fallback, and to validate that model-returned timestamps stay within the transcript's
/// actual bounds. Not a general-purpose subtitle library - just what this feature needs.
/// </summary>
public static class SrtTranscript
{
    private static readonly Regex TimeRangePattern = new(
        @"(\d{2}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2})[,.](\d{3})",
        RegexOptions.Compiled);

    /// <summary>Parses SRT text into cues. Malformed blocks are skipped rather than throwing.</summary>
    public static List<SrtCue> Parse(string srt)
    {
        var cues = new List<SrtCue>();
        if (string.IsNullOrWhiteSpace(srt))
        {
            return cues;
        }

        // SRT blocks are separated by a blank line; normalise line endings first.
        var blocks = srt.Replace("\r\n", "\n").Replace('\r', '\n').Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                continue;
            }

            // First line is normally the cue index, but tolerate its absence.
            var timeLineIndex = int.TryParse(lines[0], out _) ? 1 : 0;
            if (timeLineIndex >= lines.Length)
            {
                continue;
            }

            var match = TimeRangePattern.Match(lines[timeLineIndex]);
            if (!match.Success)
            {
                continue;
            }

            var start = ToTimeSpan(match, 1);
            var end = ToTimeSpan(match, 5);
            var text = string.Join(' ', lines.Skip(timeLineIndex + 1));

            var index = timeLineIndex == 1 && int.TryParse(lines[0], out var parsedIndex)
                ? parsedIndex
                : cues.Count + 1;

            cues.Add(new SrtCue(index, start, end, text));
        }

        return cues;
    }

    private static TimeSpan ToTimeSpan(Match match, int groupOffset) => new(
        0,
        int.Parse(match.Groups[groupOffset].Value, CultureInfo.InvariantCulture),
        int.Parse(match.Groups[groupOffset + 1].Value, CultureInfo.InvariantCulture),
        int.Parse(match.Groups[groupOffset + 2].Value, CultureInfo.InvariantCulture),
        int.Parse(match.Groups[groupOffset + 3].Value, CultureInfo.InvariantCulture));

    /// <summary>The last cue's end time, used as a lower-bound estimate of video duration when none is known.</summary>
    public static TimeSpan? GetDuration(IReadOnlyList<SrtCue> cues) =>
        cues.Count == 0 ? null : cues[^1].End;

    /// <summary>
    /// Splits cues into chunks of roughly <paramref name="targetCharsPerChunk"/> characters each,
    /// never splitting a single cue across chunks, preserving chronological order.
    /// </summary>
    public static List<List<SrtCue>> Chunk(IReadOnlyList<SrtCue> cues, int targetCharsPerChunk)
    {
        var chunks = new List<List<SrtCue>>();
        var current = new List<SrtCue>();
        var currentChars = 0;

        foreach (var cue in cues)
        {
            if (current.Count > 0 && currentChars + cue.Text.Length > targetCharsPerChunk)
            {
                chunks.Add(current);
                current = [];
                currentChars = 0;
            }

            current.Add(cue);
            currentChars += cue.Text.Length;
        }

        if (current.Count > 0)
        {
            chunks.Add(current);
        }

        return chunks;
    }

    /// <summary>Renders cues back into a compact "[seconds] text" transcript the model can read, one line per cue.</summary>
    public static string ToPlainTimestampedText(IEnumerable<SrtCue> cues)
    {
        var builder = new StringBuilder();
        foreach (var cue in cues)
        {
            builder.Append('[').Append((int)cue.Start.TotalSeconds).Append("] ").AppendLine(cue.Text);
        }

        return builder.ToString();
    }

    /// <summary>Formats whole seconds as HH:MM:SS, matching the format the model is asked to produce.</summary>
    public static string FormatSeconds(int totalSeconds) =>
        TimeSpan.FromSeconds(Math.Max(0, totalSeconds)).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}
