using System.Net.Http.Json;
using System.Text.Json;

namespace docusystem.Services;

/// <summary>
/// Laravel API helper using <see cref="IHttpClientFactory"/> (named client <c>LaravelApi</c>) and <see cref="ApiEndpointOptions"/> for the base address.
/// Prefer this over <c>new HttpClient()</c> so connections are pooled and the bearer handler applies.
/// </summary>
public sealed class ApiService : IApiService
{
	private const string ClientName = "LaravelApi";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly AppSessionService _session;
	private readonly IAuthService _authService;

	public ApiService(
		IHttpClientFactory httpClientFactory,
		AppSessionService session,
		IAuthService authService)
	{
		_httpClientFactory = httpClientFactory;
		_session = session;
		_authService = authService;
	}

	/// <inheritdoc />
	public void SetAuthToken(string? token) => _session.SetAccessToken(token);

	/// <inheritdoc />
	public Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default) =>
		_authService.LoginAsync(email, password, cancellationToken);

	public async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken = default) where T : class
	{
		var client = _httpClientFactory.CreateClient(ClientName);
		var path = NormalizePath(relativePath);
		using var response = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
		{
			return null;
		}

		return JsonSerializer.Deserialize<T>(body, JsonOptions);
	}

	public async Task<TResponse?> PostAsync<TResponse>(string relativePath, object payload, CancellationToken cancellationToken = default) where TResponse : class
	{
		var client = _httpClientFactory.CreateClient(ClientName);
		var path = NormalizePath(relativePath);
		using var response = await client.PostAsJsonAsync(path, payload, cancellationToken).ConfigureAwait(false);
		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
		{
			return null;
		}

		return JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
	}

	public async Task<TResponse?> PutAsync<TResponse>(string relativePath, object payload, CancellationToken cancellationToken = default) where TResponse : class
	{
		var client = _httpClientFactory.CreateClient(ClientName);
		var path = NormalizePath(relativePath);
		using var response = await client.PutAsJsonAsync(path, payload, cancellationToken).ConfigureAwait(false);
		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
		{
			return null;
		}

		return JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
	}

	public Task<HttpResponseMessage> PostAsync(string relativePath, object payload, CancellationToken cancellationToken = default)
	{
		var client = _httpClientFactory.CreateClient(ClientName);
		var path = NormalizePath(relativePath);
		return client.PostAsJsonAsync(path, payload, cancellationToken);
	}

	public Task<HttpResponseMessage> GetRawAsync(string relativePath, CancellationToken cancellationToken = default)
	{
		var client = _httpClientFactory.CreateClient(ClientName);
		var path = NormalizePath(relativePath);
		return client.GetAsync(path, cancellationToken);
	}

	/// <summary>Paths are relative to <see cref="ApiEndpointOptions.LaravelBaseUrl"/>, e.g. <c>api/proposals/pending</c>.</summary>
	private static string NormalizePath(string relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath))
		{
			return string.Empty;
		}

		return relativePath.Trim().TrimStart('/');
	}
}
