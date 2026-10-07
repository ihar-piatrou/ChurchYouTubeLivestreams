using ChurchYouTubeAssistant.Exceptions;
using ChurchYouTubeAssistant.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace ChurchYouTubeAssistant.Controllers;

/// <summary>
/// Google's OAuth 2.0 redirect target. The route must match the authorised redirect URI registered
/// on the OAuth client exactly: https://localhost:5001/oauth2/callback in development.
/// </summary>
/// <remarks>
/// This endpoint's only real caller is a browser following Google's redirect, so it redirects back
/// to the dashboard (Home/Index) with a flash message rather than returning JSON - there is no
/// script or test relying on a JSON response here, and a human looking at a browser should land on
/// a page, not a JSON blob. The JSON API under <c>/youtube</c> is unaffected: <c>GET /youtube/channel</c>,
/// <c>/youtube/videos</c> and <c>/youtube/status</c> still return JSON exactly as before.
/// </remarks>
[Route("oauth2")]
public sealed class OAuthController(
    IGoogleOAuthService oauthService,
    IYouTubeReadService youTubeReadService,
    ILogger<OAuthController> logger) : Controller
{
    /// <summary>
    /// Handles the redirect back from Google: validates state, exchanges the authorization code for
    /// tokens, then confirms the connection by reading the channel, before redirecting to "/".
    /// </summary>
    /// <param name="code">The authorization code, present on success.</param>
    /// <param name="state">The CSRF state value we issued at /youtube/connect.</param>
    /// <param name="error">Google's error code, present when the user denied consent.</param>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            // Google sends error=access_denied when the user clicks Cancel, and also when an
            // External app in Testing mode is used by an account that is not a Test User.
            logger.LogWarning("Google OAuth callback returned error '{Error}'.", error);

            // Burn the state so the abandoned authorization cannot be resumed by anyone else.
            oauthService.AbandonAuthorization(state);

            var detail = string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
                ? "Authorization was denied. If you did not cancel, confirm that this Google account " +
                  "is listed as a Test User on the OAuth consent screen while the app is in Testing mode."
                : $"Google reported '{error}' instead of completing authorization.";

            return RedirectToDashboard(error: detail);
        }

        // State validation, the code exchange and token storage all happen here. Anything wrong
        // throws an OAuthFlowException - caught here (rather than left to the central JSON handler)
        // so the browser gets a message on the dashboard instead of a ProblemDetails body.
        Models.YouTubeConnectionStatusDto status;
        try
        {
            status = await oauthService.CompleteAuthorizationAsync(code, state, cancellationToken);
        }
        catch (OAuthFlowException ex)
        {
            logger.LogWarning(ex, "OAuth callback failed.");
            return RedirectToDashboard(error: ex.Message);
        }

        var warnings = new List<string>();
        if (!status.CanRefreshUnattended)
        {
            warnings.Add(
                "Google did not return a refresh token, so unattended processing will stop when the " +
                "access token expires. Revoke access at https://myaccount.google.com/permissions and " +
                "connect again to obtain one.");
        }

        // Read the channel immediately: it proves end to end that the token works, and it is what
        // populates the dashboard the admin is about to land on.
        try
        {
            await youTubeReadService.GetAuthenticatedChannelAsync(cancellationToken);
        }
        catch (NoYouTubeChannelException ex)
        {
            // The tokens are valid and worth keeping; the account simply has no channel.
            warnings.Add(ex.Message);
        }
        catch (YouTubeIntegrationException ex)
        {
            logger.LogWarning(ex, "Connected successfully but could not read the channel.");
            warnings.Add("Connected, but reading the channel failed. Reload the dashboard to retry.");
        }

        return RedirectToDashboard(success: "Connected to YouTube.", warnings: warnings);
    }

    private IActionResult RedirectToDashboard(
        string? success = null, string? error = null, IReadOnlyList<string>? warnings = null)
    {
        var query = new Dictionary<string, string?>();
        if (success is not null) query["success"] = success;
        if (error is not null) query["error"] = error;
        if (warnings is { Count: > 0 }) query["warnings"] = string.Join('|', warnings);

        return Redirect(QueryHelpers.AddQueryString("/", query));
    }
}
