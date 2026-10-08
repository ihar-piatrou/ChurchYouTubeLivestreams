using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChurchYouTubeAssistant.Configuration;
using ChurchYouTubeAssistant.Exceptions;
using Microsoft.Extensions.Options;

namespace ChurchYouTubeAssistant.Ai;

/// <inheritdoc cref="IThumbnailImageService"/>
/// <remarks>
/// Ideogram's generate endpoint responds with a URL to the generated image, not the image bytes
/// themselves - so one call here means two HTTP requests: generate, then download. The download
/// request intentionally does not carry the Ideogram API key; Ideogram's returned URLs are
/// pre-signed/temporary asset links that do not require it.
/// </remarks>
public sealed class IdeogramThumbnailService(
    HttpClient httpClient,
    IOptions<IdeogramOptions> options,
    ILogger<IdeogramThumbnailService> logger) : IThumbnailImageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IdeogramOptions _options = options.Value;

    public async Task<ThumbnailImageResult> GenerateAsync(
        string imagePrompt, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            throw new ThumbnailGenerationException(
                ThumbnailGenerationError.Configuration,
                "Ideogram:ApiKey is not configured. Set it with: " +
                "dotnet user-secrets set \"Ideogram:ApiKey\" \"YOUR_IDEOGRAM_API_KEY\"");
        }

        if (string.IsNullOrWhiteSpace(imagePrompt))
        {
            throw new ThumbnailGenerationException(
                ThumbnailGenerationError.TransientFailure,
                "This analysis has no thumbnail image prompt to generate from.");
        }

        using var timeoutCts = new CancellationTokenSource(_options.RequestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var imageUrl = await RequestGenerationAsync(imagePrompt, linkedCts.Token, timeoutCts, cancellationToken);
        return await DownloadImageAsync(imageUrl, linkedCts.Token);
    }

    private async Task<string> RequestGenerationAsync(
        string imagePrompt,
        CancellationToken linkedToken,
        CancellationTokenSource timeoutCts,
        CancellationToken callerToken)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(imagePrompt), "prompt" },
            { new StringContent(_options.Size), "size" },
            { new StringContent(_options.Quality), "quality" }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"v2/image/generate/{_options.Model}")
        {
            Content = form
        };
        request.Headers.Add("Api-Key", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, linkedToken);
        }
        catch (Exception ex) when (timeoutCts.IsCancellationRequested && !callerToken.IsCancellationRequested)
        {
            // Same "trust the token, not the exception shape" approach used for the OpenAI call.
            throw new ThumbnailGenerationException(
                ThumbnailGenerationError.Timeout,
                $"The Ideogram request timed out after {_options.RequestTimeout}.", ex);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ThumbnailGenerationException(
                ThumbnailGenerationError.TransientFailure, $"The Ideogram request failed: {ex.Message}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(linkedToken);
                logger.LogError(
                    "Ideogram generate call failed: {StatusCode} {Body}", response.StatusCode, body);

                var (error, message) = response.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests =>
                        (ThumbnailGenerationError.RateLimited, "Ideogram rate-limited the request. Try again shortly."),
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                        (ThumbnailGenerationError.Configuration,
                         "Ideogram rejected the API key. Check the Ideogram:ApiKey User Secret."),
                    _ => (ThumbnailGenerationError.TransientFailure,
                          $"Ideogram returned {(int)response.StatusCode}: {Truncate(body, 300)}")
                };

                throw new ThumbnailGenerationException(error, message);
            }

            IdeogramGenerateResponse? parsed;
            try
            {
                parsed = await response.Content.ReadFromJsonAsync<IdeogramGenerateResponse>(
                    JsonOptions, linkedToken);
            }
            catch (JsonException ex)
            {
                throw new ThumbnailGenerationException(
                    ThumbnailGenerationError.InvalidResponse, "Ideogram's response was not valid JSON.", ex);
            }

            var imageUrl = parsed?.Data?.FirstOrDefault()?.Url;
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                throw new ThumbnailGenerationException(
                    ThumbnailGenerationError.InvalidResponse,
                    "Ideogram did not return an image URL. The prompt may have been flagged by Ideogram's " +
                    "safety check, or Ideogram's response shape has changed since this was written.");
            }

            return imageUrl;
        }
    }

    private async Task<ThumbnailImageResult> DownloadImageAsync(string imageUrl, CancellationToken cancellationToken)
    {
        HttpResponseMessage imageResponse;
        try
        {
            imageResponse = await httpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, imageUrl), cancellationToken);
        }
        catch (Exception ex)
        {
            throw new ThumbnailGenerationException(
                ThumbnailGenerationError.TransientFailure,
                $"Could not download the generated image from Ideogram: {ex.Message}", ex);
        }

        using (imageResponse)
        {
            if (!imageResponse.IsSuccessStatusCode)
            {
                throw new ThumbnailGenerationException(
                    ThumbnailGenerationError.TransientFailure,
                    $"Downloading the generated image failed ({(int)imageResponse.StatusCode}).");
            }

            var bytes = await imageResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = imageResponse.Content.Headers.ContentType?.MediaType ?? "image/png";

            logger.LogInformation(
                "Generated a {ContentType} thumbnail image ({Bytes} bytes) via Ideogram.", contentType, bytes.Length);

            return new ThumbnailImageResult(bytes, contentType);
        }
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";

    private sealed record IdeogramGenerateResponse
    {
        [JsonPropertyName("data")]
        public List<IdeogramImageData>? Data { get; init; }
    }

    private sealed record IdeogramImageData
    {
        [JsonPropertyName("url")]
        public string? Url { get; init; }
    }
}
