using ChurchYouTubeAssistant.Ai;
using Xunit;

namespace ChurchYouTubeAssistant.Tests.Ai;

public sealed class SrtTranscriptTests
{
    private const string SampleSrt = """
        1
        00:00:00,000 --> 00:00:02,500
        Hello and welcome.

        2
        00:00:02,500 --> 00:00:05,000
        Today we talk about legacy.

        3
        00:18:40,000 --> 00:18:45,000
        Что мы передадим следующему поколению?
        """;

    [Fact]
    public void Parse_ValidSrt_ReturnsCuesInOrderWithCorrectTimestamps()
    {
        var cues = SrtTranscript.Parse(SampleSrt);

        Assert.Equal(3, cues.Count);
        Assert.Equal(TimeSpan.Zero, cues[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(2.5), cues[0].End);
        Assert.Equal("Hello and welcome.", cues[0].Text);
        Assert.Equal(new TimeSpan(0, 0, 18, 40), cues[2].Start);
        Assert.Equal("Что мы передадим следующему поколению?", cues[2].Text);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsNoCues()
    {
        Assert.Empty(SrtTranscript.Parse(""));
        Assert.Empty(SrtTranscript.Parse("   "));
    }

    [Fact]
    public void Parse_MalformedBlock_IsSkippedRatherThanThrowing()
    {
        const string malformed = "1\nnot a timestamp\nSome text";

        var cues = SrtTranscript.Parse(malformed);

        Assert.Empty(cues);
    }

    [Fact]
    public void GetDuration_ReturnsLastCuesEndTime()
    {
        var cues = SrtTranscript.Parse(SampleSrt);

        var duration = SrtTranscript.GetDuration(cues);

        Assert.Equal(new TimeSpan(0, 0, 18, 45), duration);
    }

    [Fact]
    public void GetDuration_NoCues_ReturnsNull()
    {
        Assert.Null(SrtTranscript.GetDuration([]));
    }

    [Fact]
    public void Chunk_NeverSplitsASingleCueAcrossChunks()
    {
        var cues = SrtTranscript.Parse(SampleSrt);

        // Target chunk size smaller than a single cue's text to force many small chunks.
        var chunks = SrtTranscript.Chunk(cues, targetCharsPerChunk: 5);

        var totalCuesAcrossChunks = chunks.Sum(c => c.Count);
        Assert.Equal(cues.Count, totalCuesAcrossChunks);
        // Every cue must appear whole in exactly one chunk, never split.
        Assert.All(chunks, chunk => Assert.True(chunk.Count >= 1));
    }

    [Fact]
    public void Chunk_PreservesChronologicalOrderAcrossChunkBoundaries()
    {
        var cues = SrtTranscript.Parse(SampleSrt);
        var chunks = SrtTranscript.Chunk(cues, targetCharsPerChunk: 10);

        var flattened = chunks.SelectMany(c => c).ToList();
        for (var i = 1; i < flattened.Count; i++)
        {
            Assert.True(flattened[i].Start >= flattened[i - 1].Start);
        }
    }

    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(65, "00:01:05")]
    [InlineData(3661, "01:01:01")]
    public void FormatSeconds_ProducesHhMmSs(int seconds, string expected)
    {
        Assert.Equal(expected, SrtTranscript.FormatSeconds(seconds));
    }
}
