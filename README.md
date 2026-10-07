# Church YouTube Assistant

Backend foundation for automating post-processing of church YouTube livestreams and sermon videos.

**Stage 1 (this build): Google OAuth → connect the church YouTube channel → read the channel and its
latest uploads.** Read-only, by design: the only scope requested is
`https://www.googleapis.com/auth/youtube.readonly`, so this build is *incapable* of modifying the
channel.

Not implemented yet (deliberately): AI analysis, FFmpeg frame extraction, thumbnail generation,
database persistence, approval workflow, `videos.update` / `thumbnails.set`.

---

## 1. Prerequisites

* .NET 10 SDK (the repo pins `10.0.201` via `global.json`)
* A Google Cloud project with YouTube Data API v3 enabled and a **Web application** OAuth client
  whose authorised redirect URI is exactly `https://localhost:5001/oauth2/callback`

## 2. Configure secrets (one time)

`ClientId` and `ClientSecret` come from .NET User Secrets. They are never read from
`appsettings.json`, and the downloaded `client_secret*.json` from Google Cloud is **not** needed at
runtime.

```bash
cd ChurchYouTubeAssistant
dotnet user-secrets init                                           # already done in this repo
dotnet user-secrets set "Google:ClientId"     "YOUR_CLIENT_ID"
dotnet user-secrets set "Google:ClientSecret" "YOUR_CLIENT_SECRET"
```

`Google:RedirectUri` lives in `appsettings.Development.json` (it is not a secret, but it must match
the Google OAuth client byte for byte or Google returns `redirect_uri_mismatch`).

Startup validates all three and **fails fast** with the exact command to run if one is missing.

## 3. Trust the local HTTPS certificate

Google requires HTTPS for a web-application redirect URI. If the browser warns about the
certificate, or the app cannot start an HTTPS listener:

```bash
dotnet dev-certs https --trust
```

Check the current state with `dotnet dev-certs https --check --trust`.

## 4. Run

```bash
dotnet run --project ChurchYouTubeAssistant
```

The app listens on **https://localhost:5001** and http://localhost:5000. The ports are pinned in the
`Kestrel` section of `appsettings.Development.json` (not `launchSettings.json`) so that 5001 binds
however the app is launched — the OAuth redirect URI depends on it.

## 5. Connect the channel

1. Open <https://localhost:5001/youtube/connect> **in a browser**.
2. Sign in with the Google account that owns the church channel and approve YouTube read access.
3. Google redirects to `/oauth2/callback`, which validates state, exchanges the code for tokens,
   stores them and returns a summary including the channel it just connected.
4. Verify:
   * <https://localhost:5001/youtube/channel>
   * <https://localhost:5001/youtube/videos?maxResults=10>
   * <https://localhost:5001/youtube/videos?maxResults=10&includeDetails=true>

---

## Endpoints

| Method | Route | Purpose |
|---|---|---|
| GET | `/youtube/connect` | Starts OAuth; 302 to Google's consent screen |
| GET | `/oauth2/callback` | Google's redirect target: validates state, exchanges code, stores tokens |
| GET | `/youtube/status` | Whether YouTube is connected and whether it can refresh unattended |
| GET | `/youtube/channel` | Authenticated channel: id, title, description, custom URL, thumbnail, subscriber/video/view counts, uploads playlist id |
| GET | `/youtube/videos` | Latest uploads (`maxResults` 1-50, default 10; `includeDetails=true` adds duration, stats, captions and livestream timings) |
| DELETE | `/youtube/connection` | Revokes the grant at Google and forgets the stored tokens |
| GET | `/openapi/v1.json` | OpenAPI document (Development only) |

Failures are RFC 7807 `ProblemDetails`:

| Situation | Status |
|---|---|
| YouTube not connected / refresh token no longer valid | 409 Conflict |
| Authorized Google account has no YouTube channel | 404 Not Found |
| Consent denied, invalid or replayed `state`, missing code, scope withheld | 400 Bad Request |
| Google rejected the token exchange | 502 Bad Gateway |
| YouTube Data API failed (quota, forbidden, …) | 502 Bad Gateway |

---

## Architecture

```
Controllers/        HTTP only: translate requests to service calls, no OAuth or YouTube logic
  YouTubeController.cs      /youtube/*
  OAuthController.cs        /oauth2/callback
Services/
  GoogleOAuthService.cs     authorization URL, code exchange, refresh, token lifecycle
  YouTubeReadService.cs     YouTube Data API v3 calls and DTO mapping
Auth/
  IOAuthStateStore.cs       CSRF state: issue + single-use verification
  IYouTubeTokenStore.cs     token persistence boundary
  DevelopmentYouTubeTokenStore.cs   in-memory, NOT FOR PRODUCTION
  NoOpGoogleDataStore.cs    stops Google's SDK writing tokens to disk itself
Configuration/      GoogleOAuthOptions + startup validator (fail fast)
Models/             our own DTOs; Google SDK types never cross the API boundary
Infrastructure/     ApiExceptionHandler: domain exception -> ProblemDetails, in one place
Exceptions/         YouTubeNotConnected, NoYouTubeChannel, OAuthFlow, YouTubeIntegration
```

### Decisions that matter for the next stages

**OAuth is Google's library, storage and CSRF state are ours.**
`GoogleAuthorizationCodeFlow` (Google.Apis.Auth) does the protocol work. Its data store is wired to
`NoOpGoogleDataStore` so the SDK cannot persist tokens behind our back — the default writes plaintext
JSON into the user's home directory. `IYouTubeTokenStore` is the single source of truth.

**`access_type=offline` + `prompt=consent`.**
Offline access is what makes Google willing to issue a refresh token; `prompt=consent` is what makes
it *actually do so*. Google returns a refresh token only on an authorization where consent was
granted, so a returning admin who already approved these scopes would otherwise be sent straight
through with an access token alone — and unattended processing would break an hour later. The cost is
one extra consent screen on each reconnect, which is the right trade for a rare admin action. To get
a refresh token after a consent-free authorization, revoke access at
<https://myaccount.google.com/permissions> and connect again.

**Testing mode expires refresh tokens after 7 days.** While the OAuth app is in *Testing*, Google
invalidates refresh tokens weekly; the refresh then fails with `invalid_grant` and the API answers
409 asking for a reconnect. Publishing the app (or moving to an Internal/Workspace audience) removes
that limit — required before unattended processing is reliable.

**Read and write stay separate.** The service is `IYouTubeReadService`, not `IYouTubeService`. When
`videos.update` and `thumbnails.set` arrive they belong in a separate write-side service behind a
separate scope and the approval step, so the ability to publish to the church channel is an explicit
dependency rather than an accident of class membership.

**Token storage is a seam, not a detail.** Replacing the development store with encrypted database
storage (or AWS Secrets Manager / Azure Key Vault) is one DI line in `Program.cs`. Nothing else
changes. The in-memory store loses tokens on restart and cannot work across instances — both must be
fixed before deployment.

**State storage is in-process too.** Fine for an interactive admin action on one instance; move to a
distributed cache (Redis/ElastiCache) or a signed, short-lived encrypted state value when scaled out.
Only `MemoryCacheOAuthStateStore` changes.

**Access tokens are refreshed centrally.** `GoogleOAuthService.GetAccessTokenAsync` refreshes 5
minutes ahead of expiry, serialises concurrent refreshes behind a semaphore, and the YouTube service
retries once with a forced refresh on a 401. Background processing in later stages gets this for free.

**Quota.** Enumerating videos uses the channel's uploads playlist: `channels.list` (1 unit) +
`playlistItems.list` (1 unit), versus 100 units for a `search.list` call. `includeDetails=true` adds
one batched `videos.list` (1 unit for the whole page, regardless of id count). Default quota is
10,000 units/day.

**Hooks for the pipeline already in place.** `includeDetails=true` returns `isLivestream`,
`liveActualStartTime`, `liveActualEndTime` and `hasCaptions` — the fields that stage 2 (detect newly
completed livestreams) and stage 3 (fetch transcripts) need to decide what to process.

### Secrets hygiene

* `ClientId`/`ClientSecret` only from User Secrets or the platform secret store.
* `.gitignore` blocks `client_secret*.json`, `credentials*.json`, `service-account*.json`,
  `token*.json`, `.env*` and `appsettings.Local.json`.
* `OAuthTokenInfo.ToString()` is overridden to redact tokens, because a record's generated
  `ToString()` would otherwise print the access and refresh tokens into any log line.
* Nothing logs a token or the client secret; OAuth failures log Google's `error`/`error_description`
  only.

### Suggested next step

There is no test project yet. The highest-value first tests are `GoogleOAuthService` token refresh
and expiry arithmetic (it already takes `TimeProvider`, so time is injectable) and
`MemoryCacheOAuthStateStore` single-use semantics.
