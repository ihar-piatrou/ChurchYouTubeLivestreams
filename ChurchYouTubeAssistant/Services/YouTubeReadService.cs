using System.Net;
using System.Text;
using System.Xml;
using ChurchYouTubeAssistant.Caching;
using ChurchYouTubeAssistant.Exceptions;
using ChurchYouTubeAssistant.Models;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using GoogleYouTubeService = Google.Apis.YouTube.v3.YouTubeService;

namespace ChurchYouTubeAssistant.Services;

/// <summary>
/// YouTube Data API v3 integration. Knows nothing about HTTP endpoints, and nothing about how the
/// OAuth tokens it uses were obtained or stored.
/// </summary>
public sealed class YouTubeReadService(
    IGoogleOAuthService oauthService,
    IVideoCacheStore videoCache,
    ILogger<YouTubeReadService> logger) : IYouTubeReadService
{
    /// <summary>Sent to Google as the User-Agent; useful when reading API quota/error reports.</summary>
    private const string ApplicationName = "Church YouTube Assistant";

    /// <summary>The YouTube Data API refuses pages larger than 50.</summary>
    private const int MaxPageSize = 50;

    public async Task<YouTubeChannelDto> GetAuthenticatedChannelAsync(
        CancellationToken cancellationToken = default)
    {
        var channel = await GetChannelResourceAsync(cancellationToken);
        return MapChannel(channel);
    }

    public async Task<YouTubeVideoDto> GetVideoAsync(
        string videoId, CancellationToken cancellationToken = default)
    {
        var video = await ExecuteAsync(
            async (api, ct) =>
            {
                var request = api.Videos.List("snippet,contentDetails");
                request.Id = new List<string> { videoId };
                var response = await request.ExecuteAsync(ct);
                return response.Items?.FirstOrDefault();
            },
            "retrieving a single video",
            cancellationToken);

        if (video is null)
        {
            throw new YouTubeIntegrationException(
                $"Video {videoId} was not found on the connected channel.");
        }

        return new YouTubeVideoDto
        {
            VideoId = video.Id,
            Title = video.Snippet?.Title ?? "(untitled video)",
            Description = video.Snippet?.Description,
            PublishedAt = video.Snippet?.PublishedAtDateTimeOffset,
            ThumbnailUrl = PickThumbnail(video.Snippet?.Thumbnails),
            Details = new YouTubeVideoDetailsDto
            {
                Duration = ParseIso8601Duration(video.ContentDetails?.Duration)
            }
        };
    }

    public async Task<YouTubeVideoListDto> GetLatestVideosAsync(
        int maxResults = 10,
        bool includeDetails = false,
        CancellationToken cancellationToken = default)
    {
        var requested = Math.Clamp(maxResults, 1, MaxPageSize);

        var channel = await GetChannelResourceAsync(cancellationToken);
        var uploadsPlaylistId = channel.ContentDetails?.RelatedPlaylists?.Uploads;

        if (string.IsNullOrWhiteSpace(uploadsPlaylistId))
        {
            throw new YouTubeIntegrationException(
                "The channel did not report an uploads playlist, so its videos cannot be listed.");
        }

        // playlistItems.list costs 1 quota unit and already carries the title, description,
        // thumbnails and publish time. search.list would cost 100 units for the same answer.
        var items = await ExecuteAsync(
            async (api, ct) =>
            {
                var request = api.PlaylistItems.List("snippet,contentDetails");
                request.PlaylistId = uploadsPlaylistId;
                request.MaxResults = requested;
                var response = await request.ExecuteAsync(ct);
                return response.Items ?? [];
            },
            "listing the uploads playlist",
            cancellationToken);

        var videos = items
            .Select(MapPlaylistItem)
            .Where(video => !string.IsNullOrWhiteSpace(video.VideoId))
            .ToList();

        if (includeDetails && videos.Count > 0)
        {
            videos = await AttachDetailsAsync(videos, cancellationToken);
        }

        // Opportunistic: this data came back "for free" as part of the list call, so cache it now
        // rather than waiting for something else to ask for it later. It does not save quota on
        // this call itself, but it builds up the durable per-video record (title/description) that
        // the transcript cache then attaches to.
        foreach (var video in videos)
        {
            await videoCache.UpsertAsync(
                video.VideoId,
                new VideoRecordUpdate
                {
                    Title = video.Title,
                    Description = video.Description,
                    PublishedAt = video.PublishedAt,
                    ThumbnailUrl = video.ThumbnailUrl
                },
                cancellationToken);
        }

        logger.LogInformation(
            "Retrieved {Count} video(s) from uploads playlist of channel {ChannelId}.",
            videos.Count,
            channel.Id);

        return new YouTubeVideoListDto
        {
            ChannelId = channel.Id,
            UploadsPlaylistId = uploadsPlaylistId,
            Count = videos.Count,
            Videos = videos
        };
    }

    public async Task<YouTubeTranscriptDto> GetVideoTranscriptAsync(
        string videoId,
        string? languageCode = null,
        string format = "srt",
        CancellationToken cancellationToken = default)
    {
        var cached = await videoCache.GetAsync(videoId, cancellationToken);
        if (TryBuildCachedTranscript(cached, videoId, languageCode, format) is { } cachedTranscript)
        {
            logger.LogInformation(
                "Serving the transcript for video {VideoId} from the local cache; no YouTube API call made.",
                videoId);
            return cachedTranscript;
        }

        var tracks = await ListCaptionTracksAsync(videoId, cancellationToken);

        if (tracks.Count == 0)
        {
            throw new TranscriptNotAvailableException(
                $"Video {videoId} has no caption tracks at all.");
        }

        var selected = SelectTrack(tracks, languageCode);
        if (selected is null)
        {
            var availableLanguages = string.Join(", ", tracks
                .Select(track => track.Snippet?.Language)
                .Where(language => !string.IsNullOrWhiteSpace(language))
                .Distinct());

            throw new TranscriptNotAvailableException(
                $"Video {videoId} has no caption track in language '{languageCode}'. " +
                $"Available languages: {availableLanguages}.");
        }

        var content = await DownloadCaptionTrackAsync(selected.Id, format, cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            // Google can return 200 OK with zero bytes for a track that looks selectable in
            // captions.list but is not actually ready: still "syncing", or an auto-generated track
            // the API declines to export despite accepting the request. Treat that the same as "no
            // transcript" rather than silently handing back a technically-successful empty result.
            throw new TranscriptNotAvailableException(
                $"Video {videoId} has a caption track ({selected.Snippet?.Language ?? "unknown language"}, " +
                $"status: {selected.Snippet?.Status ?? "unknown"}), but YouTube returned no content for it. " +
                "This can happen for a track still processing, or an auto-generated track YouTube restricts " +
                "from being downloaded via the API.");
        }

        logger.LogInformation(
            "Retrieved {Format} transcript for video {VideoId}, track {CaptionTrackId} ({Language}, auto-generated: {IsAutoGenerated}).",
            format,
            videoId,
            selected.Id,
            selected.Snippet?.Language,
            IsAutoGenerated(selected));

        await videoCache.UpsertAsync(
            videoId,
            new VideoRecordUpdate
            {
                TranscriptCaptionTrackId = selected.Id,
                TranscriptContent = content,
                TranscriptLanguage = selected.Snippet?.Language,
                TranscriptFormat = format,
                TranscriptIsAutoGenerated = IsAutoGenerated(selected),
                TranscriptFetchedAtUtc = DateTimeOffset.UtcNow
            },
            cancellationToken);

        return new YouTubeTranscriptDto
        {
            VideoId = videoId,
            CaptionTrackId = selected.Id,
            Language = selected.Snippet?.Language,
            Name = selected.Snippet?.Name,
            IsAutoGenerated = IsAutoGenerated(selected),
            Format = format,
            Content = content
        };
    }

    /// <summary>
    /// Builds a transcript DTO from a cached record, if it has a transcript matching what was asked
    /// for. Returns null when there is nothing cached, or what's cached doesn't match the requested
    /// language/format - in which case the caller falls through to a fresh API fetch.
    /// </summary>
    private static YouTubeTranscriptDto? TryBuildCachedTranscript(
        VideoRecord? cached, string videoId, string? languageCode, string format)
    {
        if (cached?.TranscriptContent is not { Length: > 0 } content)
        {
            return null;
        }

        if (!string.Equals(cached.TranscriptFormat, format, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (languageCode is not null
            && !string.Equals(cached.TranscriptLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new YouTubeTranscriptDto
        {
            VideoId = videoId,
            CaptionTrackId = cached.TranscriptCaptionTrackId ?? "(cached)",
            Language = cached.TranscriptLanguage,
            Name = null,
            IsAutoGenerated = cached.TranscriptIsAutoGenerated ?? false,
            Format = cached.TranscriptFormat ?? format,
            Content = content
        };
    }

    /// <summary>captions.list: the caption tracks available for one video. 50 quota units.</summary>
    private async Task<IReadOnlyList<Caption>> ListCaptionTracksAsync(
        string videoId,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            async (api, ct) =>
            {
                var request = api.Captions.List("snippet", videoId);
                var response = await request.ExecuteAsync(ct);
                return (IReadOnlyList<Caption>)(response.Items ?? []);
            },
            "listing caption tracks",
            cancellationToken);

    /// <summary>
    /// captions.download: the actual subtitle file content for one track. 200 quota units - by far
    /// the most expensive call this service makes, so transcripts should be fetched once and cached
    /// by the caller rather than re-requested.
    /// </summary>
    private async Task<string> DownloadCaptionTrackAsync(
        string captionId,
        string format,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            async (api, ct) =>
            {
                var request = api.Captions.Download(captionId);
                request.Tfmt = format;

                using var stream = new MemoryStream();

                // Unlike every other call in this class, a download does not throw on failure: it
                // reports a failed IDownloadProgress instead. Translate that back into the same
                // exception shape ExecuteAsync already knows how to classify and log.
                var progress = await request.DownloadAsync(stream, ct);
                if (progress.Status == DownloadStatus.Failed)
                {
                    if (progress.Exception is GoogleApiException googleEx)
                    {
                        throw googleEx;
                    }

                    throw new YouTubeIntegrationException(
                        "Downloading the caption track failed.", progress.Exception);
                }

                stream.Position = 0;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return await reader.ReadToEndAsync(ct);
            },
            "downloading the caption track",
            cancellationToken);

    /// <summary>
    /// Picks a caption track: filters to the requested language if given, drops tracks YouTube has
    /// already marked "failed" (never downloadable), then prefers one that is actually ready to
    /// serve over one still "syncing", and prefers a manually created/uploaded track over YouTube's
    /// auto-generated (ASR) one among equally-ready candidates.
    /// </summary>
    /// <remarks>
    /// Checking <c>status</c> matters: captions.list can list a track that captions.download
    /// happily "succeeds" on with zero bytes, because the track is still processing. Ranking by
    /// readiness first is what keeps that from being picked over a track that actually has content.
    /// </remarks>
    private static Caption? SelectTrack(IReadOnlyList<Caption> tracks, string? languageCode)
    {
        var candidates = string.IsNullOrWhiteSpace(languageCode)
            ? tracks
            : tracks
                .Where(track => string.Equals(track.Snippet?.Language, languageCode, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        // "failed" tracks are never downloadable; exclude them unless they are literally all we have,
        // in which case the empty-content guard in GetVideoTranscriptAsync will report it clearly.
        var usable = candidates
            .Where(track => !string.Equals(track.Snippet?.Status, "failed", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (usable.Count == 0)
        {
            usable = candidates.ToList();
        }

        return usable
            .OrderByDescending(IsServing)
            .ThenBy(IsAutoGenerated)
            .First();
    }

    /// <summary>snippet.status is "serving" once the track actually has downloadable content.</summary>
    private static bool IsServing(Caption track) =>
        string.Equals(track.Snippet?.Status, "serving", StringComparison.OrdinalIgnoreCase);

    /// <summary>snippet.trackKind is "ASR" for YouTube's own speech-recognition track, "standard" otherwise.</summary>
    private static bool IsAutoGenerated(Caption track) =>
        string.Equals(track.Snippet?.TrackKind, "asr", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// channels.list with mine=true: the authorized account's own channel. 1 quota unit.
    /// </summary>
    private async Task<Channel> GetChannelResourceAsync(CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(
            async (api, ct) =>
            {
                var request = api.Channels.List("snippet,contentDetails,statistics");
                request.Mine = true;
                return await request.ExecuteAsync(ct);
            },
            "retrieving the authenticated channel",
            cancellationToken);

        var channel = response.Items?.FirstOrDefault();
        if (channel is null)
        {
            throw new NoYouTubeChannelException(
                "The authorized Google account does not have a YouTube channel. Connect again with " +
                "the Google account that owns the church channel, or create a channel for it first.");
        }

        return channel;
    }

    /// <summary>
    /// One batched videos.list call (1 quota unit regardless of how many ids or parts) to enrich an
    /// already-retrieved page of videos.
    /// </summary>
    private async Task<List<YouTubeVideoDto>> AttachDetailsAsync(
        List<YouTubeVideoDto> videos,
        CancellationToken cancellationToken)
    {
        var ids = videos.Select(video => video.VideoId).ToList();

        var detailsById = await ExecuteAsync(
            async (api, ct) =>
            {
                var request = api.Videos.List("contentDetails,statistics,status,liveStreamingDetails");
                request.Id = ids;
                var response = await request.ExecuteAsync(ct);
                return (response.Items ?? []).ToDictionary(video => video.Id, MapVideoDetails);
            },
            "retrieving video details",
            cancellationToken);

        return videos
            .Select(video => detailsById.TryGetValue(video.VideoId, out var details)
                ? video with { Details = details }
                : video)
            .ToList();
    }

    /// <summary>
    /// Runs a YouTube API call with a freshly obtained access token, retrying once on 401 with a
    /// forced token refresh. A 401 here means Google considers the token invalid even though our
    /// expiry bookkeeping did not, which one refresh resolves.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(
        Func<GoogleYouTubeService, CancellationToken, Task<T>> operation,
        string description,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var accessToken = await oauthService.GetAccessTokenAsync(
                forceRefresh: attempt > 0, cancellationToken);

            // The client is created per call because it is bound to a single access token.
            // The OAuth service owns refreshing, so the SDK never needs our refresh token.
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
                    ex,
                    "YouTube API call failed while {Description}: {StatusCode} {Reason}.",
                    description,
                    ex.HttpStatusCode,
                    ex.Error?.Message);

                throw new YouTubeIntegrationException(
                    BuildApiFailureMessage(ex, description),
                    ex);
            }
        }
    }

    private static string BuildApiFailureMessage(GoogleApiException ex, string description)
    {
        var reason = ex.Error?.Errors?.FirstOrDefault()?.Reason;

        return reason switch
        {
            "quotaExceeded" or "rateLimitExceeded" =>
                "The YouTube Data API quota for this Google Cloud project is exhausted. " +
                "Try again after the daily quota resets.",
            "forbidden" or "insufficientPermissions" =>
                "YouTube refused the request as unauthorized. The granted scopes may be insufficient, " +
                "or the authorized account may not own this channel. Reconnect at /youtube/connect.",
            _ => $"The YouTube Data API failed while {description} ({ex.HttpStatusCode})."
        };
    }

    private static YouTubeChannelDto MapChannel(Channel channel) => new()
    {
        ChannelId = channel.Id,
        Title = channel.Snippet?.Title ?? "(untitled channel)",
        Description = channel.Snippet?.Description,
        CustomUrl = channel.Snippet?.CustomUrl,
        ThumbnailUrl = PickThumbnail(channel.Snippet?.Thumbnails),
        CreatedAt = channel.Snippet?.PublishedAtDateTimeOffset,
        SubscriberCount = channel.Statistics?.SubscriberCount,
        SubscriberCountHidden = channel.Statistics?.HiddenSubscriberCount ?? false,
        VideoCount = channel.Statistics?.VideoCount,
        ViewCount = channel.Statistics?.ViewCount,
        UploadsPlaylistId = channel.ContentDetails?.RelatedPlaylists?.Uploads
    };

    private static YouTubeVideoDto MapPlaylistItem(PlaylistItem item) => new()
    {
        // contentDetails.videoId is the canonical id; snippet.resourceId is the older spelling.
        VideoId = item.ContentDetails?.VideoId ?? item.Snippet?.ResourceId?.VideoId ?? string.Empty,
        Title = item.Snippet?.Title ?? "(untitled video)",
        Description = item.Snippet?.Description,

        // videoPublishedAt is when the video went live; snippet.publishedAt is when it was added
        // to the playlist, which differs for imported or re-added uploads.
        PublishedAt = item.ContentDetails?.VideoPublishedAtDateTimeOffset
                      ?? item.Snippet?.PublishedAtDateTimeOffset,
        ThumbnailUrl = PickThumbnail(item.Snippet?.Thumbnails)
    };

    private static YouTubeVideoDetailsDto MapVideoDetails(Video video) => new()
    {
        Duration = ParseIso8601Duration(video.ContentDetails?.Duration),
        ViewCount = video.Statistics?.ViewCount,
        LikeCount = video.Statistics?.LikeCount,
        PrivacyStatus = video.Status?.PrivacyStatus,
        LiveBroadcastContent = video.Snippet?.LiveBroadcastContent,
        IsLivestream = video.LiveStreamingDetails is not null,
        LiveActualStartTime = video.LiveStreamingDetails?.ActualStartTimeDateTimeOffset,
        LiveActualEndTime = video.LiveStreamingDetails?.ActualEndTimeDateTimeOffset,
        HasCaptions = ParseApiBoolean(video.ContentDetails?.Caption)
    };

    /// <summary>Highest resolution thumbnail available, falling back down the ladder.</summary>
    private static string? PickThumbnail(ThumbnailDetails? thumbnails) =>
        thumbnails?.Maxres?.Url
        ?? thumbnails?.Standard?.Url
        ?? thumbnails?.High?.Url
        ?? thumbnails?.Medium?.Url
        ?? thumbnails?.Default__?.Url;

    /// <summary>contentDetails.duration is an ISO 8601 duration such as PT1H2M3S.</summary>
    private static TimeSpan? ParseIso8601Duration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return null;
        }

        try
        {
            return XmlConvert.ToTimeSpan(duration);
        }
        catch (FormatException)
        {
            // Live or still-processing videos can report durations we cannot parse (e.g. "P0D").
            return null;
        }
    }

    /// <summary>contentDetails.caption is the string "true"/"false", not a JSON boolean.</summary>
    private static bool? ParseApiBoolean(string? value) =>
        bool.TryParse(value, out var parsed) ? parsed : null;
}
