using Microsoft.Extensions.Options;

namespace ChurchYouTubeAssistant.Configuration;

/// <summary>
/// Fails fast at startup with actionable messages when OAuth configuration is missing or malformed.
/// Registered with ValidateOnStart() so a misconfigured app never reaches a browser.
/// </summary>
public sealed class GoogleOAuthOptionsValidator : IValidateOptions<GoogleOAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, GoogleOAuthOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add(
                "Google:ClientId is not configured. Set it with: " +
                "dotnet user-secrets set \"Google:ClientId\" \"YOUR_CLIENT_ID\"");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add(
                "Google:ClientSecret is not configured. Set it with: " +
                "dotnet user-secrets set \"Google:ClientSecret\" \"YOUR_CLIENT_SECRET\" " +
                "(never put the client secret in appsettings.json)");
        }

        if (string.IsNullOrWhiteSpace(options.RedirectUri))
        {
            failures.Add(
                "Google:RedirectUri is not configured. It must exactly match an authorised redirect URI " +
                "on the Google OAuth client, e.g. https://localhost:5001/oauth2/callback");
        }
        else if (!Uri.TryCreate(options.RedirectUri, UriKind.Absolute, out var redirectUri))
        {
            failures.Add($"Google:RedirectUri ('{options.RedirectUri}') is not an absolute URI.");
        }
        else if (redirectUri.Scheme != Uri.UriSchemeHttps && !redirectUri.IsLoopback)
        {
            failures.Add(
                $"Google:RedirectUri ('{options.RedirectUri}') must use https except on loopback. " +
                "Google rejects non-HTTPS redirect URIs for web application clients.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
