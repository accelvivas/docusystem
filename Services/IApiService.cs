using docusystem.Models;

namespace docusystem.Services;

/// <summary>Typed HTTP access to the Laravel API — uses the named <c>LaravelApi</c> <see cref="System.Net.Http.HttpClient"/> and session bearer token.</summary>
public interface IApiService
{
	/// <summary>Stores the token so <see cref="LaravelAuthDelegatingHandler"/> sends <c>Authorization: Bearer</c> on the next requests.</summary>
	void SetAuthToken(string? token);

	/// <summary>POST <c>api/login</c> — delegates to <see cref="IAuthService"/> so there is a single implementation.</summary>
	Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);

	Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken = default) where T : class;

	Task<TResponse?> PostAsync<TResponse>(string relativePath, object payload, CancellationToken cancellationToken = default) where TResponse : class;

	Task<TResponse?> PutAsync<TResponse>(string relativePath, object payload, CancellationToken cancellationToken = default) where TResponse : class;

	Task<HttpResponseMessage> PostAsync(string relativePath, object payload, CancellationToken cancellationToken = default);

	Task<HttpResponseMessage> GetRawAsync(string relativePath, CancellationToken cancellationToken = default);
}
