using System.ClientModel;
using System.Text.Json;
using ChurchYouTubeAssistant.Exceptions;
using ChurchYouTubeAssistant.Infrastructure;
using ChurchYouTubeAssistant.Models;
using OpenAI.Chat;
using Polly;

namespace ChurchYouTubeAssistant.Services;

/// <inheritdoc cref="ICategorizationService"/>
/// <remarks>
/// Uses the same OpenAI ChatClient singleton as Ai.OpenAiVideoAnalysisService (model and API key
/// already baked in at registration), but with its own small schema/prompt - a categorization call
/// needs none of the transcript-chunking/multi-stage-fallback machinery the main analysis call
/// needs, since the input here is a short summary, never the full transcript. Shares the same
/// <see cref="OpenAiRetryPolicy"/> retry pipeline too, since it hits the same rate limits.
/// </remarks>
public sealed class OpenAiCategorizationService(ChatClient chatClient, ResiliencePipeline retryPipeline) : ICategorizationService
{
    /// <summary>Keeps the category badge row on the Analysis page readable - a long tail of "also
    /// touches on" categories is noise, not signal, for the "what do we cover more" chart.</summary>
    private const int MaxSecondaryCategories = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string CategoryNamesJson =
        JsonSerializer.Serialize(TheologyCategoryCatalog.All.Select(c => c.EnglishName).ToList());

    private static readonly string SchemaJson = $$"""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "primaryCategory": { "type": "string", "enum": {{CategoryNamesJson}} },
            "secondaryCategories": { "type": "array", "items": { "type": "string", "enum": {{CategoryNamesJson}} } }
          },
          "required": ["primaryCategory", "secondaryCategories"]
        }
        """;

    private static readonly string SystemPrompt = BuildSystemPrompt();

    public async Task<CategorizationResult> CategorizeAsync(
        VideoAnalysis analysis, CancellationToken cancellationToken = default)
    {
        var messages = new ChatMessage[]
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(BuildUserPrompt(analysis))
        };

        var chatOptions = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "sermon_category",
                jsonSchema: BinaryData.FromString(SchemaJson),
                jsonSchemaFormatDescription: "Sermon theology-category classification",
                jsonSchemaIsStrict: true),
            // The default model (OpenAI:Model, "gpt-5") is a reasoning model: hidden reasoning tokens
            // draw from the same budget as the visible output, so a too-small limit here can be
            // entirely consumed before any JSON is written - the exact failure mode
            // OpenAiVideoAnalysisService already guards against for the main analysis call. Minimal
            // effort because this is a simple closed-set classification, not deep analysis - and a
            // generous token ceiling as a safety margin even at minimal effort.
            MaxOutputTokenCount = 2000,
#pragma warning disable OPENAI001 // ChatReasoningEffortLevel is an experimental SDK API.
            ReasoningEffortLevel = ChatReasoningEffortLevel.Minimal
#pragma warning restore OPENAI001
        };

        ClientResult<ChatCompletion> response;
        try
        {
            response = await retryPipeline.ExecuteAsync(
                async ct => await chatClient.CompleteChatAsync(messages, chatOptions, ct), cancellationToken);
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            throw new AiAnalysisException(
                AiAnalysisError.RateLimited, "OpenAI rate-limited the categorization request. Try again shortly.", ex);
        }
        catch (ClientResultException ex) when (ex.Status is 401 or 403)
        {
            throw new AiAnalysisException(
                AiAnalysisError.Configuration, "OpenAI rejected the API key. Check the OpenAI:ApiKey User Secret.", ex);
        }
        catch (ClientResultException ex)
        {
            throw new AiAnalysisException(
                AiAnalysisError.TransientFailure, $"The OpenAI categorization request failed (HTTP {ex.Status}).", ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AiAnalysisException(
                AiAnalysisError.TransientFailure, $"The OpenAI categorization request failed unexpectedly: {ex.Message}", ex);
        }

        var completion = response.Value;
        if (completion.Content.Count == 0 || string.IsNullOrWhiteSpace(completion.Content[0].Text))
        {
            throw new AiAnalysisException(AiAnalysisError.InvalidResponse, "OpenAI returned no categorization content.");
        }

        var parsed = JsonSerializer.Deserialize<CategorizationJson>(completion.Content[0].Text, JsonOptions)
            ?? throw new AiAnalysisException(AiAnalysisError.InvalidResponse, "OpenAI's categorization response could not be parsed.");

        var primary = TheologyCategoryCatalog.TryParse(parsed.PrimaryCategory)
            ?? throw new AiAnalysisException(
                AiAnalysisError.InvalidResponse, $"OpenAI returned an unrecognized category: '{parsed.PrimaryCategory}'.");

        var secondary = parsed.SecondaryCategories
            .Select(TheologyCategoryCatalog.TryParse)
            .Where(c => c is not null && c != primary)
            .Select(c => c!.Value)
            .Distinct()
            .Take(MaxSecondaryCategories)
            .ToList();

        return new CategorizationResult(primary, secondary);
    }

    private static string BuildUserPrompt(VideoAnalysis analysis)
    {
        var bibleContext = string.Join('\n',
            analysis.BibleReferences.Where(r => r.IsPrimary).Select(r => $"- {r.Reference}: {r.Context}"));

        return $"""
            SERMON LANGUAGE: {analysis.Language ?? "unknown"}
            MAIN MESSAGE: {analysis.MainMessage}
            MAIN QUESTION: {analysis.MainQuestion}
            KEY THEMES: {string.Join(", ", analysis.KeyThemes)}
            KEYWORDS: {string.Join(", ", analysis.Keywords)}
            PRIMARY BIBLE REFERENCES:
            {bibleContext}
            """;
    }

    private static string BuildSystemPrompt()
    {
        var options = string.Join('\n', TheologyCategoryCatalog.All.Select(c => $"- {c.EnglishName} ({c.RussianName})"));

        return $"""
            You classify a Christian sermon into a fixed systematic-theology taxonomy, based only on
            the summary given to you (not the full transcript). Always output category names using
            their exact English label below, never a translation or your own wording, even though
            the sermon summary may be in Russian.

            TAXONOMY (20 fixed categories):
            {options}

            - primaryCategory: exactly one of the above - the single best fit for the sermon's main
              thrust. Required - pick the closest match even if the fit is imperfect; never omit it.
            - secondaryCategories: at most 2 additional categories the sermon MEANINGFULLY also
              touches on - not every tangential mention, excluding whichever one you chose as
              primaryCategory. Leave the array empty, or with just one entry, unless a second
              category is genuinely as central to the sermon as the first.
            """;
    }

    private sealed record CategorizationJson
    {
        public string PrimaryCategory { get; init; } = string.Empty;
        public List<string> SecondaryCategories { get; init; } = [];
    }
}
