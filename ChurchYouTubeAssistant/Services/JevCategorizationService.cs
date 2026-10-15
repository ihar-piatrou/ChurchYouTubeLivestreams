using System.Net.Http.Json;
using System.Text.Json;
using ChurchYouTubeAssistant.Configuration;
using ChurchYouTubeAssistant.Exceptions;
using ChurchYouTubeAssistant.Models;
using Microsoft.Extensions.Options;

namespace ChurchYouTubeAssistant.Services;

/// <inheritdoc cref="ICategorizationService"/>
/// <remarks>
/// Calls Jev (TypeSafe AI's "System One" model, https://docs.typesafe.ai) at POST /v1/systemone
/// with a single "choice" question whose criteria are the 20 TheologyCategory options. The chosen
/// option becomes PrimaryCategory; Jev's Choice response also returns a probability for every
/// option (they sum to 1), so secondary categories are derived from that same single call rather
/// than asking 20 separate yes/no ("noul") questions - any option other than the primary one whose
/// probability clears <see cref="SecondaryProbabilityThreshold"/> is treated as a secondary match.
/// <para>
/// Written directly against the documented HTTP contract (no official .NET SDK exists as of this
/// writing - Jev ships Python/JavaScript SDKs only). Not yet exercised against a live Jev account:
/// wire up Jev:ApiKey and flip Categorization:Provider to "Jev" to start using it; until then
/// OpenAiCategorizationService remains the default (see Program.cs).
/// </para>
/// </remarks>
public sealed class JevCategorizationService(HttpClient httpClient, IOptions<JevOptions> options)
    : ICategorizationService
{
    /// <summary>
    /// Any category other than the chosen primary whose Choice probability is at least this high
    /// counts as a secondary category. Not documented by Jev as a standard cutoff - a starting
    /// heuristic to tune once real classifications can be reviewed.
    /// </summary>
    private const double SecondaryProbabilityThreshold = 0.15;

    /// <summary>Keeps the category badge row on the Analysis page readable.</summary>
    private const int MaxSecondaryCategories = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly JevOptions _options = options.Value;

    public async Task<CategorizationResult> CategorizeAsync(
        VideoAnalysis analysis, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            throw new AiAnalysisException(
                AiAnalysisError.Configuration,
                "Jev:ApiKey is not configured. Set it with: " +
                "dotnet user-secrets set \"Jev:ApiKey\" \"YOUR_JEV_API_KEY\"");
        }

        var criteria = TheologyCategoryCatalog.All.ToDictionary(c => c.EnglishName, c => c.RussianName);

        var request = new SystemOneRequest
        {
            State = BuildState(analysis),
            Model = _options.Model,
            Questions = new Dictionary<string, ChoiceQuestion>
            {
                ["category"] = new ChoiceQuestion
                {
                    Instructions =
                        "Which systematic-theology category best fits this sermon's main thrust? The " +
                        "sermon summary may be in Russian or English; criteria descriptions are in Russian.",
                    Criteria = criteria
                }
            }
        };

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "v1/systemone")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var timeoutCts = new CancellationTokenSource(_options.RequestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await httpClient.SendAsync(requestMessage, linkedCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (timeoutCts.IsCancellationRequested)
        {
            throw new AiAnalysisException(
                AiAnalysisError.Timeout, $"The Jev categorization request timed out after {_options.RequestTimeout}.", ex);
        }
        catch (Exception ex)
        {
            throw new AiAnalysisException(
                AiAnalysisError.TransientFailure, $"The Jev categorization request failed unexpectedly: {ex.Message}", ex);
        }

        using (httpResponse)
        {
            if (!httpResponse.IsSuccessStatusCode)
            {
                var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                throw (int)httpResponse.StatusCode switch
                {
                    401 or 403 => new AiAnalysisException(
                        AiAnalysisError.Configuration, "Jev rejected the API key. Check the Jev:ApiKey User Secret."),
                    429 => new AiAnalysisException(
                        AiAnalysisError.RateLimited, "Jev rate-limited the categorization request. Try again shortly."),
                    _ => new AiAnalysisException(
                        AiAnalysisError.TransientFailure,
                        $"The Jev request failed (HTTP {(int)httpResponse.StatusCode}): {body}")
                };
            }

            var result = await httpResponse.Content.ReadFromJsonAsync<SystemOneResponse>(JsonOptions, cancellationToken)
                ?? throw new AiAnalysisException(AiAnalysisError.InvalidResponse, "Jev returned an empty response.");

            if (!result.Answers.TryGetValue("category", out var answer))
            {
                throw new AiAnalysisException(AiAnalysisError.InvalidResponse, "Jev's response had no 'category' answer.");
            }

            var primary = TheologyCategoryCatalog.TryParse(answer.Choice)
                ?? throw new AiAnalysisException(
                    AiAnalysisError.InvalidResponse, $"Jev returned an unrecognized category: '{answer.Choice}'.");

            var secondary = answer.Probabilities
                .Where(kv => kv.Value >= SecondaryProbabilityThreshold)
                .OrderByDescending(kv => kv.Value)
                .Select(kv => TheologyCategoryCatalog.TryParse(kv.Key))
                .Where(c => c is not null && c != primary)
                .Select(c => c!.Value)
                .Take(MaxSecondaryCategories)
                .ToList();

            return new CategorizationResult(primary, secondary);
        }
    }

    private static string BuildState(VideoAnalysis analysis)
    {
        var bibleContext = string.Join('\n',
            analysis.BibleReferences.Where(r => r.IsPrimary).Select(r => $"- {r.Reference}: {r.Context}"));

        return $"""
            Sermon language: {analysis.Language ?? "unknown"}
            Main message: {analysis.MainMessage}
            Main question: {analysis.MainQuestion}
            Key themes: {string.Join(", ", analysis.KeyThemes)}
            Keywords: {string.Join(", ", analysis.Keywords)}
            Primary Bible references:
            {bibleContext}
            """;
    }

    private sealed record SystemOneRequest
    {
        public required string State { get; init; }
        public required string Model { get; init; }
        public required Dictionary<string, ChoiceQuestion> Questions { get; init; }
    }

    private sealed record ChoiceQuestion
    {
        public string Type { get; init; } = "choice";
        public required string Instructions { get; init; }
        public required Dictionary<string, string> Criteria { get; init; }
    }

    private sealed record SystemOneResponse
    {
        public string Model { get; init; } = string.Empty;
        public Dictionary<string, ChoiceAnswer> Answers { get; init; } = [];
    }

    private sealed record ChoiceAnswer
    {
        public string Type { get; init; } = string.Empty;
        public string Choice { get; init; } = string.Empty;
        public double Confidence { get; init; }
        public Dictionary<string, double> Probabilities { get; init; } = [];
    }
}
