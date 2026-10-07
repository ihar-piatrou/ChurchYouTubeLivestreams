using System.ClientModel;
using System.Text.Json;
using ChurchYouTubeAssistant.Configuration;
using ChurchYouTubeAssistant.Exceptions;
using ChurchYouTubeAssistant.Models.Ai;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace ChurchYouTubeAssistant.Ai;

/// <summary>
/// OpenAI implementation of <see cref="IVideoAnalysisAiService"/>, using the Chat Completions API
/// with structured outputs (strict JSON Schema) so the response is guaranteed to match
/// <see cref="VideoAnalysisResult"/>'s shape rather than merely being asked to.
/// </summary>
/// <remarks>
/// <para><b>Single-request path (default):</b> the whole transcript plus existing metadata is sent
/// in one call. This is what section 2 of the spec requires as the default.</para>
/// <para><b>Multi-stage fallback:</b> triggered only when the estimated token count exceeds
/// <see cref="OpenAiOptions.SingleRequestTokenBudget"/>. The transcript is split into
/// timestamp-preserving chunks (see <see cref="SrtTranscript.Chunk"/>); each chunk is analyzed with
/// the *same* schema and prompt (the chunk's own title/description/thumbnail output is discarded -
/// only its bibleReferences/chapters/shorts/keyThemes are kept, since those are the fields a partial
/// transcript can answer meaningfully). The per-chunk results are merged and deduplicated, then one
/// final lightweight call - using the merged themes/references/chapters as context instead of the
/// full transcript again - produces the holistic title/description/thumbnail/sermonAnalysis. This
/// keeps the "one logical operation" experience from the caller's point of view while bounding the
/// size of any single request. It is a simpler map-reduce than a fully tuned pipeline would be;
/// see the project README for known limitations.
/// </para>
/// </remarks>
public sealed class OpenAiVideoAnalysisService(
    ChatClient chatClient,
    IVideoAnalysisPromptProvider promptProvider,
    IOptions<OpenAiOptions> options,
    ILogger<OpenAiVideoAnalysisService> logger) : IVideoAnalysisAiService
{
    /// <summary>
    /// Conservative chars-per-token estimate. Mixed Cyrillic/Latin text does not tokenize as
    /// efficiently as English, so this errs toward overestimating token count (triggering the
    /// fallback sooner than strictly necessary) rather than underestimating it. Not a real
    /// tokenizer - swap for one (e.g. a BPE-aware token counter) if more precision is needed.
    /// </summary>
    private const double EstimatedCharsPerToken = 2.5;

    /// <summary>Target chunk size for the multi-stage fallback, in characters of cue text.</summary>
    private const int ChunkTargetChars = 30_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OpenAiOptions _options = options.Value;

    public async Task<VideoAnalysisAiCallResult> AnalyzeAsync(
        VideoAnalysisInput input, CancellationToken cancellationToken = default)
    {
        var estimatedTokens = EstimateTokens(promptProvider.SystemPrompt) + EstimateTokens(input.Transcript);

        if (estimatedTokens <= _options.SingleRequestTokenBudget)
        {
            var (result, rawJson, usage) = await CallOpenAiAsync(
                promptProvider.SystemPrompt, BuildUserPrompt(input, input.Transcript), cancellationToken);

            return new VideoAnalysisAiCallResult
            {
                Result = result,
                RawResponseJson = rawJson,
                Model = _options.Model,
                PromptVersion = promptProvider.Version,
                PromptTokens = usage?.InputTokenCount,
                CompletionTokens = usage?.OutputTokenCount,
                TotalTokens = usage?.TotalTokenCount,
                UsedMultiStageFallback = false
            };
        }

        logger.LogInformation(
            "Transcript for video {VideoId} is ~{EstimatedTokens} estimated tokens, over the {Budget} single-request " +
            "budget; using the multi-stage fallback.",
            input.VideoId, estimatedTokens, _options.SingleRequestTokenBudget);

        return await AnalyzeWithFallbackAsync(input, cancellationToken);
    }

    private async Task<VideoAnalysisAiCallResult> AnalyzeWithFallbackAsync(
        VideoAnalysisInput input, CancellationToken cancellationToken)
    {
        var cues = SrtTranscript.Parse(input.Transcript);
        var chunks = SrtTranscript.Chunk(cues, ChunkTargetChars);

        if (chunks.Count == 0)
        {
            throw new AiAnalysisException(
                AiAnalysisError.InvalidResponse, "The transcript could not be parsed into any timestamped cues.");
        }

        var chunkResults = new List<VideoAnalysisResult>();
        var usages = new List<ChatTokenUsage>();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunkText = SrtTranscript.ToPlainTimestampedText(chunks[i]);
            var chunkPrompt =
                $"NOTE: This is segment {i + 1} of {chunks.Count} of a longer recording. Analyze ONLY this " +
                "segment's content for bibleReferences, chapters, shorts and sermonAnalysis.keyThemes/keywords. " +
                "The metadata, mainMessage, mainQuestion and thumbnail fields are not used from segment calls - " +
                "fill them with your best short guess for this segment only; they will be discarded by the caller.\n\n" +
                BuildUserPrompt(input, chunkText);

            var (chunkResult, _, usage) = await CallOpenAiAsync(promptProvider.SystemPrompt, chunkPrompt, cancellationToken);
            chunkResults.Add(chunkResult);
            if (usage is not null)
            {
                usages.Add(usage);
            }

            logger.LogDebug(
                "Analyzed segment {Segment}/{Total} for video {VideoId}: {Chapters} chapter(s), {Shorts} Short(s).",
                i + 1, chunks.Count, input.VideoId, chunkResult.Chapters.Count, chunkResult.Shorts.Count);
        }

        var merged = MergeChunkResults(chunkResults);

        var synthesisPrompt = BuildSynthesisPrompt(input, merged);
        var (synthesized, rawSynthesisJson, synthesisUsage) =
            await CallOpenAiAsync(promptProvider.SystemPrompt, synthesisPrompt, cancellationToken);
        if (synthesisUsage is not null)
        {
            usages.Add(synthesisUsage);
        }

        // Take the holistic fields from the synthesis call; keep the richer, transcript-grounded
        // bibleReferences/chapters/shorts/keyThemes gathered from the real per-chunk passes.
        var final = new VideoAnalysisResult
        {
            Language = synthesized.Language,
            Metadata = synthesized.Metadata,
            SermonAnalysis = new SermonAnalysis
            {
                MainMessage = synthesized.SermonAnalysis.MainMessage,
                MainQuestion = synthesized.SermonAnalysis.MainQuestion,
                KeyThemes = merged.SermonAnalysis.KeyThemes,
                Keywords = merged.SermonAnalysis.Keywords
            },
            BibleReferences = merged.BibleReferences,
            Chapters = merged.Chapters,
            Thumbnail = synthesized.Thumbnail,
            Shorts = merged.Shorts
        };

        return new VideoAnalysisAiCallResult
        {
            Result = final,
            RawResponseJson = rawSynthesisJson,
            Model = _options.Model,
            PromptVersion = promptProvider.Version,
            PromptTokens = usages.Count > 0 ? usages.Sum(u => u.InputTokenCount) : null,
            CompletionTokens = usages.Count > 0 ? usages.Sum(u => u.OutputTokenCount) : null,
            TotalTokens = usages.Count > 0 ? usages.Sum(u => u.TotalTokenCount) : null,
            UsedMultiStageFallback = true
        };
    }

    /// <summary>
    /// Combines per-chunk results: concatenates and deduplicates themes/keywords/Bible references/
    /// chapters, and keeps only the highest-scored Shorts overall (chunks cannot see each other's
    /// candidates, so near-duplicates across chunk boundaries are deliberately not attempted here -
    /// a known simplification; see the service's class remarks).
    /// </summary>
    private static VideoAnalysisResult MergeChunkResults(List<VideoAnalysisResult> chunkResults)
    {
        const int maxBibleReferences = 5;
        const int maxChapters = 10;
        const int maxShorts = 10;

        return new VideoAnalysisResult
        {
            Language = chunkResults.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Language))?.Language ?? "unknown",
            SermonAnalysis = new SermonAnalysis
            {
                KeyThemes = chunkResults.SelectMany(r => r.SermonAnalysis.KeyThemes)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList(),
                Keywords = chunkResults.SelectMany(r => r.SermonAnalysis.Keywords)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToList()
            },
            BibleReferences = chunkResults.SelectMany(r => r.BibleReferences)
                .GroupBy(reference => reference.Reference, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(reference => reference.IsPrimary)
                .Take(maxBibleReferences)
                .ToList(),
            Chapters = chunkResults.SelectMany(r => r.Chapters)
                .OrderBy(chapter => chapter.StartSeconds)
                .Take(maxChapters)
                .ToList(),
            Shorts = chunkResults.SelectMany(r => r.Shorts)
                .OrderByDescending(s => s.Score)
                .Take(maxShorts)
                .ToList()
        };
    }

    private string BuildSynthesisPrompt(VideoAnalysisInput input, VideoAnalysisResult merged) =>
        "NOTE: This is the final synthesis call for a long recording that was analyzed in segments. " +
        "Instead of the full transcript, you are given the themes, Bible references and chapters already " +
        "extracted from it. Use them (and the original title/description) to produce the holistic metadata, " +
        "sermonAnalysis.mainMessage/mainQuestion and thumbnail fields. The bibleReferences/chapters/shorts " +
        "fields in your response are not used by the caller - fill them with a brief best-effort pass over " +
        "what is given below; they will be discarded.\n\n" +
        $"ORIGINAL TITLE:\n{input.OriginalTitle}\n\n" +
        $"ORIGINAL DESCRIPTION:\n{input.OriginalDescription}\n\n" +
        $"EXTRACTED THEMES: {string.Join(", ", merged.SermonAnalysis.KeyThemes)}\n\n" +
        $"EXTRACTED KEYWORDS: {string.Join(", ", merged.SermonAnalysis.Keywords)}\n\n" +
        "EXTRACTED BIBLE REFERENCES:\n" +
        string.Join('\n', merged.BibleReferences.Select(r => $"- {r.Reference}: {r.Context}")) +
        "\n\nEXTRACTED CHAPTERS:\n" +
        string.Join('\n', merged.Chapters.Select(c => $"- [{c.StartTime}] {c.Title}: {c.Summary}"));

    private static string BuildUserPrompt(VideoAnalysisInput input, string transcriptText) =>
        $"""
         VIDEO ID: {input.VideoId}
         LANGUAGE HINT: {input.LanguageHint ?? "unknown - detect from the transcript"}
         DURATION SECONDS: {(input.DurationSeconds?.ToString() ?? "unknown")}

         ORIGINAL TITLE:
         {input.OriginalTitle}

         ORIGINAL DESCRIPTION:
         {input.OriginalDescription}

         TRANSCRIPT (each line is "[seconds] text" or SRT-style cues):
         {transcriptText}
         """;

    /// <summary>Runs one OpenAI chat completion call with structured-output enforcement and maps failures.</summary>
    private async Task<(VideoAnalysisResult Result, string RawJson, ChatTokenUsage? Usage)> CallOpenAiAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var messages = new ChatMessage[] { new SystemChatMessage(systemPrompt), new UserChatMessage(userPrompt) };

        var chatOptions = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: $"video_analysis_{promptProvider.Version}",
                jsonSchema: BinaryData.FromString(promptProvider.JsonSchema),
                jsonSchemaFormatDescription: "Structured sermon video analysis",
                jsonSchemaIsStrict: true),
            MaxOutputTokenCount = _options.MaxOutputTokens,
#pragma warning disable OPENAI001 // ChatReasoningEffortLevel is an experimental SDK API.
            ReasoningEffortLevel = ParseReasoningEffort(_options.ReasoningEffort)
#pragma warning restore OPENAI001
        };

        using var timeoutCts = new CancellationTokenSource(_options.RequestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        ClientResult<ChatCompletion> response;
        try
        {
            response = await chatClient.CompleteChatAsync(messages, chatOptions, linkedCts.Token);
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            throw new AiAnalysisException(
                AiAnalysisError.RateLimited, "OpenAI rate-limited the request. Try again shortly.", ex);
        }
        catch (ClientResultException ex) when (ex.Status is 401 or 403)
        {
            throw new AiAnalysisException(
                AiAnalysisError.Configuration,
                "OpenAI rejected the API key. Check the OpenAI:ApiKey User Secret.", ex);
        }
        catch (ClientResultException ex)
        {
            throw new AiAnalysisException(
                AiAnalysisError.TransientFailure, $"The OpenAI request failed (HTTP {ex.Status}).", ex);
        }
        catch (Exception ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Checking the *token's* state rather than the exception's type/shape is deliberate:
            // the SDK's retry policy can wrap a timed-out attempt in an AggregateException
            // ("Retry failed after N tries") rather than letting a plain OperationCanceledException
            // through, and that shape must not be allowed to vary with SDK version. Whatever shape
            // it arrives in, if our own timeoutCts fired and the caller did not cancel us, the
            // request timed out - full stop.
            throw new AiAnalysisException(
                AiAnalysisError.Timeout,
                $"The OpenAI request timed out after {_options.RequestTimeout}. For a long sermon, consider " +
                "raising OpenAI:RequestTimeout in configuration.",
                ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller (not our own timeout) cancelled the operation - propagate as-is so normal
            // ASP.NET Core request-aborted handling applies, rather than reporting it as an AI failure.
            throw;
        }
        catch (Exception ex)
        {
            // Catch-all: nothing from this external dependency should ever reach the caller as a
            // raw, unhandled framework exception (an AggregateException with an internal stack
            // trace is not an answer a browser should ever see) - always surface a clean,
            // actionable AiAnalysisException instead.
            throw new AiAnalysisException(
                AiAnalysisError.TransientFailure, $"The OpenAI request failed unexpectedly: {ex.Message}", ex);
        }

        var completion = response.Value;
        var wasTruncated = completion.FinishReason == ChatFinishReason.Length;

        if (wasTruncated)
        {
            logger.LogWarning("OpenAI's response was truncated by the max output token limit.");
        }

        if (completion.Content.Count == 0 || string.IsNullOrWhiteSpace(completion.Content[0].Text))
        {
            // For reasoning models, hidden reasoning tokens are drawn from the same budget as the
            // visible output: a hard task can exhaust MaxOutputTokenCount entirely before writing
            // any JSON, which looks like "no content" but is really "ran out of room to think and
            // then answer." Name that specifically when it's the likely cause, since the fix
            // (raise the budget, or lower OpenAI:ReasoningEffort) differs from a genuine empty reply.
            var message = wasTruncated
                ? "OpenAI returned no content because the response was truncated by the max output token " +
                  "limit before any JSON was written - likely hidden reasoning tokens exhausted the budget. " +
                  $"Try raising OpenAI:MaxOutputTokens (currently {_options.MaxOutputTokens}) and/or lowering " +
                  $"OpenAI:ReasoningEffort (currently '{_options.ReasoningEffort}')."
                : "OpenAI returned no content.";

            throw new AiAnalysisException(AiAnalysisError.InvalidResponse, message);
        }

        var rawJson = completion.Content[0].Text;

        VideoAnalysisResult? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<VideoAnalysisResult>(rawJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new AiAnalysisException(
                AiAnalysisError.InvalidResponse, "OpenAI's response was not valid JSON for the expected schema.", ex);
        }

        if (parsed is null)
        {
            throw new AiAnalysisException(AiAnalysisError.InvalidResponse, "OpenAI's response deserialized to null.");
        }

        return (parsed, rawJson, completion.Usage);
    }

    private static int EstimateTokens(string text) => (int)Math.Ceiling(text.Length / EstimatedCharsPerToken);

#pragma warning disable OPENAI001 // ChatReasoningEffortLevel is an experimental SDK API.
    private ChatReasoningEffortLevel ParseReasoningEffort(string configured) => configured.Trim().ToLowerInvariant() switch
    {
        "minimal" => ChatReasoningEffortLevel.Minimal,
        "low" => ChatReasoningEffortLevel.Low,
        "medium" => ChatReasoningEffortLevel.Medium,
        "high" => ChatReasoningEffortLevel.High,
        _ => LogUnrecognisedEffortAndFallBack(configured)
    };

    private ChatReasoningEffortLevel LogUnrecognisedEffortAndFallBack(string configured)
    {
        logger.LogWarning(
            "OpenAI:ReasoningEffort '{Configured}' is not recognised (expected minimal/low/medium/high); using medium.",
            configured);
        return ChatReasoningEffortLevel.Medium;
    }
#pragma warning restore OPENAI001
}
