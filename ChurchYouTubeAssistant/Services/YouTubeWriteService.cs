using System.Net;
using ChurchYouTubeAssistant.Exceptions;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using GoogleYouTubeService = Google.Apis.YouTube.v3.YouTubeService;

namespace ChurchYouTubeAssistant.Services;

/// <summary>
/// The one write operation this application performs against the real YouTube channel. Kept
/// intentionally small and separate from <see cref="YouTubeReadService"/>.
/// </summary>
/// <remarks>
/// Mirrors YouTubeReadService's own request-execution pattern (fresh access token per call,
/// retry once on 401 with a forced refresh, map GoogleApiException to our exceptions). Duplicated
/// rather than shared for now since this class is small and the two services are deliberately
/// kept independent; extracting a common executor would be a reasonable follow-up refactor.
/// </remarks>
public sealed class YouTubeWriteService(
    IGoogleOAuthService oauthService,
    ILogger<YouTubeWriteService> logger) : IYouTubeWriteService
{
    private const string ApplicationName = "Church YouTube Assistant";

    public async Task UpdateVideoMetadataAsync(
        string videoId, string title, string description, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            async (api, ct) =>
            {
                // videos.update replaces the entire "snippet" part it is given, so the current
                // snippet must be fetched first - sending only { title, description } would wipe
                // categoryId, tags, defaultLanguage, etc.
                var listRequest = api.Videos.List("snippet");
                listRequest.Id = new List<string> { videoId };
                var listResponse = await listRequest.ExecuteAsync(ct);

                var video = listResponse.Items?.FirstOrDefault()
                    ?? throw new YouTubeIntegrationException(
                        $"Video {videoId} was not found on the connected channel; it may have been deleted " +
                        "or the connected account may not own it.");

                video.Snippet.Title = title;
                video.Snippet.Description = description;

                var updateRequest = api.Videos.Update(video, "snippet");
                await updateRequest.ExecuteAsync(ct);

                return true;
            },
            "updating video metadata",
            cancellationToken);

        logger.LogInformation("Published updated title/description to YouTube for video {VideoId}.", videoId);
    }

    public async Task SetThumbnailAsync(
        string videoId, byte[] imageData, string contentType, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            async (api, ct) =>
            {
                using var stream = new MemoryStream(imageData);
                var uploadRequest = api.Thumbnails.Set(videoId, stream, contentType);

                var progress = await uploadRequest.UploadAsync(ct);
                if (progress.Status == UploadStatus.Failed)
                {
                    // Same "rethrow the Google exception so the shared mapping/retry logic
                    // applies" pattern used for captions.download.
                    if (progress.Exception is GoogleApiException googleEx)
                    {
                        throw googleEx;
                    }

                    throw new YouTubeIntegrationException("Uploading the thumbnail image failed.", progress.Exception);
                }

                return true;
            },
            "setting the video thumbnail",
            cancellationToken);

        logger.LogInformation("Published thumbnail image to YouTube for video {VideoId}.", videoId);
    }

    private async Task<T> ExecuteAsync<T>(
        Func<GoogleYouTubeService, CancellationToken, Task<T>> operation,
        string description,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var accessToken = await oauthService.GetAccessTokenAsync(forceRefresh: attempt > 0, cancellationToken);

            using var api = new GoogleYouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
                ApplicationName = ApplicationName
            });

            try
            {
                return await operation(api, cancellationToken);
            }
            catch (GoogleApiException ex) when (attempt == 0 && ex.HttpStatusCode == HttpStatusCode.Unauthorized)
            {
                logger.LogWarning(
                    "YouTube API returned 401 while {Description}; refreshing the access token and retrying once.",
                    description);
            }
            catch (GoogleApiException ex)
            {
                logger.LogError(
                    ex, "YouTube API call failed while {Description}: {StatusCode} {Reason}.",
                    description, ex.HttpStatusCode, ex.Error?.Message);

                var reason = ex.Error?.Errors?.FirstOrDefault()?.Reason;
                var message = reason switch
                {
                    "quotaExceeded" or "rateLimitExceeded" =>
                        "The YouTube Data API quota for this project is exhausted. Try again after the daily quota resets.",
                    "forbidden" or "insufficientPermissions" =>
                        "YouTube refused the update as unauthorized. The connected account may not own this " +
                        "video, or the youtube.force-ssl scope was not granted - reconnect at /youtube/connect.",
                    _ => $"The YouTube Data API failed while {description} ({ex.HttpStatusCode})."
                };

                throw new YouTubeIntegrationException(message, ex);
            }
        }
    }
}
