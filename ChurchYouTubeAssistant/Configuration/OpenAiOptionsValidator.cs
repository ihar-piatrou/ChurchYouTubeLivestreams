using Microsoft.Extensions.Options;

namespace ChurchYouTubeAssistant.Configuration;

/// <summary>Fails fast at startup with an actionable message when the OpenAI API key is missing.</summary>
public sealed class OpenAiOptionsValidator : IValidateOptions<OpenAiOptions>
{
    public ValidateOptionsResult Validate(string? name, OpenAiOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return ValidateOptionsResult.Fail(
                "OpenAI:ApiKey is not configured. Set it with: " +
                "dotnet user-secrets set \"OpenAI:ApiKey\" \"YOUR_OPENAI_API_KEY\" " +
                "(never put it in appsettings.json).");
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            return ValidateOptionsResult.Fail("OpenAI:Model must not be empty.");
        }

        return ValidateOptionsResult.Success;
    }
}
