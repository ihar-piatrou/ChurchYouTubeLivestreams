using System.ClientModel;
using ChurchYouTubeAssistant.Configuration;
using ChurchYouTubeAssistant.Exceptions;
using Microsoft.Extensions.Options;
using OpenAI.Images;

namespace ChurchYouTubeAssistant.Ai;

/// <inheritdoc cref="IThumbnailImageService"/>
/// <remarks>
/// Unlike <see cref="IdeogramThumbnailService"/>, OpenAI's image models return the generated
/// image's bytes directly in the API response (base64) rather than a URL to download separately -
/// so there is only one HTTP call here, made via the OpenAI SDK's <see cref="ImageClient"/>.
/// </remarks>
public sealed class OpenAiThumbnailImageService(
    ImageClient imageClient,
    IOptions<OpenAiImageOptions> options,
    ILogger<OpenAiThumbnailImageService> logger) : IThumbnailImageService
{
    private readonly OpenAiImageOptions _options = options.Value;

    public async Task<ThumbnailImageResult> GenerateAsync(
        string imagePrompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePrompt))
        {
            throw new ThumbnailGenerationException(
                ThumbnailGenerationError.TransientFailure,
                "This analysis has no thumbnail image prompt to generate from.");
        }

        var generationOptions = new ImageGenerationOptions
        {
            Size = ParseSize(_options.Size),
            Quality = new GeneratedImageQuality(_options.Quality)
        };

        using var timeoutCts = new CancellationTokenSource(_options.RequestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        GeneratedImage image;
        try
        {
            var result = await imageClient.GenerateImageAsync(imagePrompt, generationOptions, linkedCts.Token);
            image = result.Value;
        }
        catch (Exception ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Same "trust the token, not the exception shape" approach used for the chat/Ideogram calls.
            throw new ThumbnailGenerationException(
                ThumbnailGenerationError.Timeout,
                $"The OpenAI image request timed out after {_options.RequestTimeout}.", ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            throw new ThumbnailGenerationException(
                ThumbnailGenerationError.RateLimited,
                "OpenAI rate-limited the image request. Try again shortly.", ex);
        }
        catch (ClientResultException ex) when (ex.Status is 401 or 403)
        {
            throw new ThumbnailGenerationException(
                ThumbnailGenerationError.Configuration,
                "OpenAI rejected the API key for image generation. Check the OpenAI:ApiKey User Secret.", ex);
        }
        catch (ClientResultException ex)
        {
            logger.LogError(ex, "OpenAI image generation call failed: {Status} {Message}", ex.Status, ex.Message);
            throw new ThumbnailGenerationException(
                ThumbnailGenerationError.TransientFailure, $"OpenAI image generation failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new ThumbnailGenerationException(
                ThumbnailGenerationError.TransientFailure, $"The OpenAI image request failed: {ex.Message}", ex);
        }

        if (image.ImageBytes is null)
        {
            throw new ThumbnailGenerationException(
                ThumbnailGenerationError.InvalidResponse,
                "OpenAI did not return image data. The prompt may have been flagged by OpenAI's " +
                "moderation check, or the response shape has changed since this was written.");
        }

        var bytes = image.ImageBytes.ToArray();
        const string contentType = "image/png";

        logger.LogInformation(
            "Generated a {ContentType} thumbnail image ({Bytes} bytes) via OpenAI ({Model}).",
            contentType, bytes.Length, _options.Model);

        return new ThumbnailImageResult(bytes, contentType);
    }

    private static GeneratedImageSize ParseSize(string size)
    {
        if (string.Equals(size, "auto", StringComparison.OrdinalIgnoreCase))
        {
#pragma warning disable OPENAI001 // GeneratedImageSize.Auto is an experimental SDK API.
            return GeneratedImageSize.Auto;
#pragma warning restore OPENAI001
        }

        // GeneratedImageSize has no string constructor (unlike GeneratedImageQuality) - only
        // (int width, int height) and a handful of named presets - so "WIDTHxHEIGHT" is parsed by
        // hand here. This also means arbitrary resolutions (which GPT Image 2.5 supports beyond
        // the old preset list) just work, with no allowlist to keep in sync with OpenAI's docs.
        var parts = size.Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && int.TryParse(parts[0], out var width)
            && int.TryParse(parts[1], out var height))
        {
            return new GeneratedImageSize(width, height);
        }

        throw new ThumbnailGenerationException(
            ThumbnailGenerationError.Configuration,
            $"OpenAiImage:Size '{size}' is not valid. Use \"WIDTHxHEIGHT\" (e.g. \"1536x1024\") or \"auto\".");
    }
}
