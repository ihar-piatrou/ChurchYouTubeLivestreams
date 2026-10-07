using Google.Apis.Util.Store;

namespace ChurchYouTubeAssistant.Auth;

/// <summary>
/// A deliberately inert <see cref="IDataStore"/> for Google's authorization flow.
/// </summary>
/// <remarks>
/// <see cref="Google.Apis.Auth.OAuth2.Flows.AuthorizationCodeFlow"/> will persist tokens itself if
/// given a data store, and its default (FileDataStore) writes them as plain JSON under the user's
/// home directory. We want exactly one source of truth, <see cref="IYouTubeTokenStore"/>, so the
/// flow is wired with this no-op instead: the flow does the OAuth protocol work, we own storage.
/// </remarks>
public sealed class NoOpGoogleDataStore : IDataStore
{
    public Task StoreAsync<T>(string key, T value) => Task.CompletedTask;

    public Task DeleteAsync<T>(string key) => Task.CompletedTask;

    public Task<T> GetAsync<T>(string key) => Task.FromResult<T>(default!);

    public Task ClearAsync() => Task.CompletedTask;
}
