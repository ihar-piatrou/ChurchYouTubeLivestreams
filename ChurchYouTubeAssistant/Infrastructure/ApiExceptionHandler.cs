using ChurchYouTubeAssistant.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ChurchYouTubeAssistant.Infrastructure;

/// <summary>
/// Single place where domain exceptions become HTTP responses, as RFC 7807 ProblemDetails.
/// </summary>
/// <remarks>
/// Keeping the mapping here means services can throw a meaningful exception from anywhere without
/// every controller repeating try/catch, and that the status code for "YouTube is not connected" is
/// decided once. Exceptions we do not recognise are passed through to the default handler, which
/// returns a bare 500 and never echoes internal detail to the caller.
/// </remarks>
public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var mapped = Map(exception);
        if (mapped is null)
        {
            return false;
        }

        var (statusCode, title) = mapped.Value;

        // Our domain exception messages are written for the admin and contain no credentials.
        // A denied consent or a replayed state is an expected 4xx, so it is logged as a single
        // warning line; only server-side failures are worth a stack trace.
        var isServerError = statusCode >= StatusCodes.Status500InternalServerError;
        logger.Log(
            isServerError ? LogLevel.Error : LogLevel.Warning,
            isServerError ? exception : null,
            "{Method} {Path} failed with {StatusCode}: {Title}. {Detail}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            statusCode,
            title,
            exception.Message);

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = exception.Message
            }
        });
    }

    private static (int StatusCode, string Title)? Map(Exception exception) => exception switch
    {
        // 409: the request was valid, the application is in the wrong state to serve it.
        YouTubeNotConnectedException =>
            (StatusCodes.Status409Conflict, "YouTube is not connected"),

        NoYouTubeChannelException =>
            (StatusCodes.Status404NotFound, "The authorized Google account has no YouTube channel"),

        TranscriptNotAvailableException =>
            (StatusCodes.Status404NotFound, "Transcript not available"),

        OAuthFlowException { Error: OAuthFlowError.RefreshFailed } =>
            (StatusCodes.Status409Conflict, "YouTube must be connected again"),

        OAuthFlowException { Error: OAuthFlowError.TokenExchangeFailed } =>
            (StatusCodes.Status502BadGateway, "Google rejected the token exchange"),

        // Bad or replayed state, a missing code, a denied or incomplete consent: caller's problem.
        OAuthFlowException =>
            (StatusCodes.Status400BadRequest, "Google authorization was not completed"),

        YouTubeIntegrationException =>
            (StatusCodes.Status502BadGateway, "The YouTube Data API request failed"),

        VideoAnalysisNotFoundException =>
            (StatusCodes.Status404NotFound, "Analysis not found"),

        AiAnalysisException { Error: AiAnalysisError.Configuration } =>
            (StatusCodes.Status500InternalServerError, "OpenAI is not configured correctly"),

        AiAnalysisException { Error: AiAnalysisError.RateLimited } =>
            (StatusCodes.Status429TooManyRequests, "OpenAI rate limit reached"),

        AiAnalysisException { Error: AiAnalysisError.Timeout } =>
            (StatusCodes.Status504GatewayTimeout, "The AI analysis request timed out"),

        AiAnalysisException =>
            (StatusCodes.Status502BadGateway, "The AI analysis request failed"),

        _ => null
    };
}
