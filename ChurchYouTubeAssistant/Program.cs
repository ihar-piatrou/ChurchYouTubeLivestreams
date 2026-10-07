using ChurchYouTubeAssistant.Ai;
using ChurchYouTubeAssistant.Auth;
using ChurchYouTubeAssistant.Caching;
using ChurchYouTubeAssistant.Configuration;
using ChurchYouTubeAssistant.Data;
using ChurchYouTubeAssistant.Infrastructure;
using ChurchYouTubeAssistant.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------------------------
// ClientId / ClientSecret come from User Secrets in development and from the platform's secret
// store (environment variables, AWS Secrets Manager, Azure Key Vault) when hosted. They are never
// read from appsettings.json, and the downloaded Google credentials JSON file is not needed.
// ValidateOnStart turns a missing value into a startup failure instead of a confusing 500 later.
builder.Services
    .AddOptions<GoogleOAuthOptions>()
    .Bind(builder.Configuration.GetSection(GoogleOAuthOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<GoogleOAuthOptions>, GoogleOAuthOptionsValidator>();

// OpenAI:ApiKey comes from User Secrets / the platform secret store, same as the Google client
// secret - never from appsettings.json.
builder.Services
    .AddOptions<OpenAiOptions>()
    .Bind(builder.Configuration.GetSection(OpenAiOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<OpenAiOptions>, OpenAiOptionsValidator>();

// ---------------------------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();

// Pending OAuth state values. In-process today; swap for a distributed cache when scaled out.
builder.Services.AddSingleton<IOAuthStateStore, MemoryCacheOAuthStateStore>();

// DEVELOPMENT token store: in-memory, not suitable for production. This single line is the
// seam where encrypted database or secret-manager storage replaces it.
builder.Services.AddSingleton<IYouTubeTokenStore, DevelopmentYouTubeTokenStore>();

// Singleton: owns the Google authorization flow (and its HttpClient) for the process lifetime.
builder.Services.AddSingleton<IGoogleOAuthService, GoogleOAuthService>();

builder.Services.AddScoped<IYouTubeReadService, YouTubeReadService>();

// Write side kept deliberately separate from IYouTubeReadService - see that interface's remarks.
builder.Services.AddScoped<IYouTubeWriteService, YouTubeWriteService>();

// Per-video cache (title/description/transcript) so the expensive YouTube calls - especially
// captions.list + captions.download at 250 of the 10,000 daily quota units - aren't repeated for
// a video already fetched. SQL Server via EF Core; swapping storage later only touches this line
// and EfVideoCacheStore, same seam IYouTubeTokenStore gives the OAuth tokens.
builder.Services.AddDbContext<ChurchYouTubeAssistantDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IVideoCacheStore, EfVideoCacheStore>();

// --- AI video analysis ---
// Prompt/schema loaded once from Prompts/VideoAnalysisV1 (see VideoAnalysisPromptProvider).
builder.Services.AddSingleton<IVideoAnalysisPromptProvider, VideoAnalysisPromptProvider>();

// ChatClient is bound to one model string; OpenAiOptions:Model is read once here. Changing the
// configured model requires a restart, which is an acceptable trade-off for a single-process app.
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;

    // The OpenAI SDK's own per-attempt network timeout defaults to 100 seconds, which a full
    // sermon analysis routinely exceeds - the SDK then retries (default: twice) and still times
    // out, wasting minutes before our own OpenAiVideoAnalysisService ever gets a chance to report
    // anything. Raise NetworkTimeout to match OpenAiOptions:RequestTimeout, and disable the SDK's
    // own retries (maxRetries: 0): our own retry/cancellation logic in OpenAiVideoAnalysisService
    // is the single source of truth for how long a call may run, rather than two independent
    // timeout mechanisms racing each other.
    var clientOptions = new OpenAIClientOptions
    {
        NetworkTimeout = opts.RequestTimeout,
        RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(maxRetries: 0)
    };

    return new ChatClient(opts.Model, new System.ClientModel.ApiKeyCredential(opts.ApiKey), clientOptions);
});
builder.Services.AddSingleton<IVideoAnalysisAiService, OpenAiVideoAnalysisService>();
builder.Services.AddSingleton<VideoAnalysisValidator>();
builder.Services.AddScoped<IVideoAnalysisService, VideoAnalysisService>();

// ---------------------------------------------------------------------------------------------
// Web
// ---------------------------------------------------------------------------------------------
// .NET's default HtmlEncoder only allows Basic Latin unescaped and numeric-entity-encodes
// everything else (including Cyrillic) as a conservative default. This app's primary content is
// Russian-language sermons, so every page would otherwise render (correctly, but unnecessarily
// bloated) as a wall of "&#x427;"-style entities. Allow the full Unicode range instead.
builder.Services.AddSingleton(
    System.Text.Encodings.Web.HtmlEncoder.Create(System.Text.Unicode.UnicodeRanges.All));

// AddControllersWithViews (not plain AddControllers) because HomeController renders Razor views
// for the simple admin UI, alongside the existing JSON API controllers.
builder.Services.AddControllersWithViews();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.MapControllers();

if (app.Environment.IsDevelopment())
{
    // Convenience for a single local dev database: create it / bring it up to date on every
    // startup, rather than requiring a manual `dotnet ef database update` first. A real deployment
    // would run migrations as an explicit release step instead of on every app start.
    using var migrationScope = app.Services.CreateScope();
    migrationScope.ServiceProvider.GetRequiredService<ChurchYouTubeAssistantDbContext>().Database.Migrate();
}

try
{
    // Reading Options.Value is what triggers validation, so it happens inside the try.
    // A one-line reminder of what the OAuth client must be configured with. No secrets are logged.
    var googleOptions = app.Services.GetRequiredService<IOptions<GoogleOAuthOptions>>().Value;
    app.Logger.LogInformation(
        "Church YouTube Assistant starting. OAuth redirect URI: {RedirectUri}; scopes: [{Scopes}]. " +
        "Dashboard: {RootUrl}; or connect directly at /youtube/connect.",
        googleOptions.RedirectUri,
        string.Join(", ", GoogleOAuthOptions.Scopes),
        googleOptions.RedirectUri.Replace("/oauth2/callback", "/", StringComparison.OrdinalIgnoreCase));

    app.Run();
}
catch (OptionsValidationException ex)
{
    // Fail fast, loudly, with the exact commands needed to fix it. Both Google OAuth and OpenAI
    // options validate on start now, so this message stays generic about which one failed.
    Console.Error.WriteLine($"Church YouTube Assistant cannot start: configuration for '{ex.OptionsType.Name}' is invalid.");
    foreach (var failure in ex.Failures)
    {
        Console.Error.WriteLine($"  - {failure}");
    }

    return 1;
}

return 0;
