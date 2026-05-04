using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using docusystem.Models;
using docusystem.Serialization;

namespace docusystem.Services;

/// <summary>
/// Login: <see cref="AuthOptions.Provider"/> <c>Supabase</c> → GoTrue email/password; <c>Laravel</c> → Sanctum API.
/// </summary>
	public sealed class AuthService : IAuthService
{
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ISessionService _session;
	private readonly AuthOptions _authOptions;
	private readonly SupabaseService _supabase;
	private readonly ApiEndpointOptions _apiOptions;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	public AuthService(
		IHttpClientFactory httpClientFactory,
		ISessionService session,
		AuthOptions authOptions,
		SupabaseService supabase,
		ApiEndpointOptions apiOptions)
	{
		_httpClientFactory = httpClientFactory;
		_session = session;
		_authOptions = authOptions;
		_supabase = supabase;
		_apiOptions = apiOptions;
	}

	public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
	{
		if (IsSupabaseAuth())
		{
			return await LoginSupabaseAsync(email, password, cancellationToken).ConfigureAwait(false);
		}

		return await LoginLaravelAsync(email, password, cancellationToken).ConfigureAwait(false);
	}

	public async Task<LoginResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
	{
		if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
		{
			return LoginResult.Fail("Email and password are required.");
		}

		// Controller (AuthController@register) validates exactly: name | email | password | password_confirmed.
		// Build a sensible "name" if the caller only sent first_name + last_name.
		var name = !string.IsNullOrWhiteSpace(request.Name)
			? request.Name!.Trim()
			: $"{request.FirstName} {request.LastName}".Trim();

		if (string.IsNullOrWhiteSpace(name))
		{
			return LoginResult.Fail("Please provide your full name.");
		}

		var client = _httpClientFactory.CreateClient("LaravelApi");

		try
		{
			using var response = await client.PostAsJsonAsync(
				"api/register",
				new
				{
					name,
					email = request.Email,
					password = request.Password,
					password_confirmation = request.PasswordConfirmation ?? request.Password
				},
				cancellationToken).ConfigureAwait(false);

			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

			if (response.IsSuccessStatusCode)
			{
				var parsed = TryParseLoginSuccess(body);
				if (parsed.User is null || string.IsNullOrWhiteSpace(parsed.User.Email))
				{
					return LoginResult.Fail("Registration succeeded but the response did not contain a user/token.");
				}

				await _session.SetFromLoginAsync(parsed.User, parsed.Token, cancellationToken).ConfigureAwait(false);
				var fromApi = await GetLaravelUserFromApiUserEndpointAsync(cancellationToken).ConfigureAwait(false);
				var finalUser = MergeLaravelUserProfile(parsed.User, fromApi) ?? parsed.User;
				NormalizeUserRoleForeignKey(finalUser);
				await _session.SetFromLoginAsync(finalUser, parsed.Token, cancellationToken).ConfigureAwait(false);
				return LoginResult.Ok(finalUser, parsed.Token);
			}

			var message = TryParseLaravelErrorMessage(body)
				?? (response.StatusCode == HttpStatusCode.UnprocessableEntity
					? "The provided details are invalid."
					: $"HTTP {(int)response.StatusCode}");

			return LoginResult.Fail(message);
		}
		catch (HttpRequestException ex)
		{
			return LoginResult.Fail($"Cannot reach {_apiOptions.LaravelBaseUrl}api/register — {ex.Message}");
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return LoginResult.Fail($"Request timed out to {_apiOptions.LaravelBaseUrl}api/register.");
		}
		catch (JsonException)
		{
			return LoginResult.Fail("Invalid response from server.");
		}
	}

	private bool IsSupabaseAuth() =>
		string.Equals(_authOptions.Provider, "Supabase", StringComparison.OrdinalIgnoreCase);

	private async Task<LoginResult> LoginSupabaseAsync(string email, string password, CancellationToken cancellationToken)
	{
		if (!_supabase.IsAvailable || _supabase.Client is null)
		{
			return LoginResult.Fail("Supabase is not configured (Url + AnonKey in appsettings).");
		}

		try
		{
			var session = await _supabase.Client.Auth.SignInWithPassword(email, password).ConfigureAwait(false);
			if (session?.User is null || string.IsNullOrWhiteSpace(session.AccessToken))
			{
				return LoginResult.Fail("Sign-in failed.");
			}

			var user = SupabaseUserMapper.ToAppUser(session.User);
			if (string.IsNullOrWhiteSpace(user.Email))
			{
				return LoginResult.Fail("Invalid user profile from Supabase.");
			}

			await _session.SetFromLoginAsync(user, session.AccessToken, cancellationToken).ConfigureAwait(false);
			return LoginResult.Ok(user, session.AccessToken);
		}
		catch (Supabase.Gotrue.Exceptions.GotrueException ex)
		{
			return LoginResult.Fail(ex.Message);
		}
		catch (Exception ex)
		{
			return LoginResult.Fail(ex.Message);
		}
	}

	private async Task<LoginResult> LoginLaravelAsync(string email, string password, CancellationToken cancellationToken)
	{
		var client = _httpClientFactory.CreateClient("LaravelApi");

		try
		{
			using var response = await client.PostAsJsonAsync(
				"api/login",
				new { email, password },
				cancellationToken).ConfigureAwait(false);

			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

			if (response.IsSuccessStatusCode)
			{
				var parsed = TryParseLoginSuccess(body);
				if (parsed.User is null || string.IsNullOrWhiteSpace(parsed.User.Email))
				{
					return LoginResult.Fail(
						$"Invalid JSON from server (expected user + token). Base: {_apiOptions.LaravelBaseUrl} " +
						$"Response preview: {TruncateForMessage(body, 200)}");
				}

				await _session.SetFromLoginAsync(parsed.User, parsed.Token, cancellationToken).ConfigureAwait(false);
				// Many backends omit role on POST /api/login; GET /api/user usually has role / role_type.
				var fromApi = await GetLaravelUserFromApiUserEndpointAsync(cancellationToken).ConfigureAwait(false);
				var finalUser = MergeLaravelUserProfile(parsed.User, fromApi) ?? parsed.User;
				NormalizeUserRoleForeignKey(finalUser);
				await _session.SetFromLoginAsync(finalUser, parsed.Token, cancellationToken).ConfigureAwait(false);
				return LoginResult.Ok(finalUser, parsed.Token);
			}

			var message = TryParseLaravelErrorMessage(body)
				?? (response.StatusCode == HttpStatusCode.Unauthorized
					? "Invalid email or password."
					: $"HTTP {(int)response.StatusCode}");

			if (body.TrimStart().StartsWith('<'))
			{
				message += " — server returned HTML (wrong URL or web login route, not /api).";
			}
			else
			{
				var hint = TruncateForMessage(body, 180);
				if (!string.IsNullOrEmpty(hint))
				{
					message += $": {hint}";
				}
			}

			return LoginResult.Fail(message);
		}
		catch (HttpRequestException ex)
		{
			return LoginResult.Fail(
				$"Cannot reach {_apiOptions.LaravelBaseUrl}api/login — {ex.Message} " +
				"(Emulator: use http://10.0.2.2:8000/ and php artisan serve --host=0.0.0.0. " +
				"Clear app data if docusystem_laravel_base_url preference overrides URL.)");
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return LoginResult.Fail(
				$"Request timed out ({(int)client.Timeout.TotalSeconds}s) to {_apiOptions.LaravelBaseUrl}api/login. " +
				"On your PC, run: php artisan serve --host=0.0.0.0 --port=8000. " +
				"Emulator must use base http://10.0.2.2:8000/ (not 127.0.0.1). " +
				"Allow port 8000 in Windows Firewall. Temporarily turn off VPN if needed.");
		}
		catch (JsonException)
		{
			return LoginResult.Fail("Invalid response from server.");
		}
	}

	private static string TruncateForMessage(string? s, int max)
	{
		if (string.IsNullOrEmpty(s))
		{
			return string.Empty;
		}

		s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
		return s.Length <= max ? s : s[..max] + "…";
	}

	public async Task LogoutAsync(CancellationToken cancellationToken = default)
	{
		if (IsSupabaseAuth() && _supabase.IsAvailable && _supabase.Client is not null)
		{
			try
			{
				await _supabase.Client.Auth.SignOut().ConfigureAwait(false);
			}
			catch
			{
				// still clear local session
			}
		}
		else
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			var token = _session.GetToken();
			if (!string.IsNullOrEmpty(token))
			{
				try
				{
					using var request = new HttpRequestMessage(HttpMethod.Post, "api/logout");
					request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
					await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
				}
				catch (HttpRequestException)
				{
				}
				catch (TaskCanceledException)
				{
				}
			}
		}

		await _session.ClearSessionAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<User?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
	{
		if (IsSupabaseAuth() && _supabase.IsAvailable && _supabase.Client is not null)
		{
			var s = _supabase.Client.Auth.CurrentSession;
			if (s?.User is null)
			{
				return _session.GetCurrentUser();
			}

			var user = SupabaseUserMapper.ToAppUser(s.User);
			await _session.SetFromLoginAsync(user, s.AccessToken, cancellationToken).ConfigureAwait(false);
			return user;
		}

		if (string.IsNullOrEmpty(_session.GetToken()))
		{
			return null;
		}

		try
		{
			var fromApi = await GetLaravelUserFromApiUserEndpointAsync(cancellationToken).ConfigureAwait(false);
			var local = _session.GetCurrentUser();
			var merged = MergeLaravelUserProfile(local, fromApi);
			NormalizeUserRoleForeignKey(merged);
			if (merged is not null && !string.IsNullOrWhiteSpace(merged.Email))
			{
				await _session
					.SetFromLoginAsync(merged, _session.GetToken(), cancellationToken)
					.ConfigureAwait(false);
			}

			return merged ?? _session.GetCurrentUser();
		}
		catch (HttpRequestException)
		{
			return _session.GetCurrentUser();
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return _session.GetCurrentUser();
		}
		catch (JsonException)
		{
			return _session.GetCurrentUser();
		}
	}

	private static readonly string[] LaravelUserGetPaths =
	{
		"api/user", "api/me", "api/v1/user", "api/profile", "user"
	};

	/// <summary>GET auth user (Sanctum) — first success wins; some apps use <c>me</c> or <c>v1</c> instead of <c>user</c>.</summary>
	private async Task<User?> GetLaravelUserFromApiUserEndpointAsync(CancellationToken cancellationToken)
	{
		var client = _httpClientFactory.CreateClient("LaravelApi");
		for (var i = 0; i < LaravelUserGetPaths.Length; i++)
		{
			var path = LaravelUserGetPaths[i];
			using var response = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				continue;
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			var user = ParseUserFromLaravelUserBody(body);
			if (user is not null && !string.IsNullOrWhiteSpace(user.Email))
			{
				return user;
			}
		}

		return null;
	}

	private User? ParseUserFromLaravelUserBody(string body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return null;
		}

		var user = ApiUserJson.DeserializeUser(body, JsonOptions);
		if (user is null)
		{
			try
			{
				user = JsonSerializer.Deserialize<User>(body, JsonOptions);
			}
			catch (JsonException)
			{
				user = null;
			}
		}

		user ??= LaravelUserCoercion.TryBuild(body, JsonOptions);
		user?.NormalizeNestedRoleFromForeignKey();
		return user;
	}

	/// <summary>Align nested <c>role</c> to <c>role_id</c> so client matches Laravel <c>currentStepForUser</c>.</summary>
	private static void NormalizeUserRoleForeignKey(User? user) =>
		user?.NormalizeNestedRoleFromForeignKey();

	private static bool HasAnyRoleData(User? u) =>
		u is not null && (
			u.UserRole is not null
			|| u.CurrentRole is not null
			|| (u.Roles is { Count: > 0 })
			|| (u.RoleId is int ri && ri > 0)
			|| (u.RoleIdCamel is int rj && rj > 0)
			|| !string.IsNullOrWhiteSpace(u.RoleType)
			|| !string.IsNullOrWhiteSpace(u.RoleTypeCamel)
			|| !string.IsNullOrWhiteSpace(u.UserRoleSlug)
			|| !string.IsNullOrWhiteSpace(u.Type)
			|| !string.IsNullOrWhiteSpace(u.AccountType));

	/// <summary>Keeps names from login when <c>api/user</c> is thin; keeps role from API when present.</summary>
	private static User? MergeLaravelUserProfile(User? fromLogin, User? fromApi)
	{
		if (fromApi is not null && !string.IsNullOrWhiteSpace(fromApi.Email))
		{
			if (fromLogin is not null)
			{
				if (string.IsNullOrWhiteSpace(fromApi.FirstName) && !string.IsNullOrWhiteSpace(fromLogin.FirstName))
				{
					fromApi.FirstName = fromLogin.FirstName;
				}

				if (string.IsNullOrWhiteSpace(fromApi.LastName) && !string.IsNullOrWhiteSpace(fromLogin.LastName))
				{
					fromApi.LastName = fromLogin.LastName;
				}

				if (string.IsNullOrWhiteSpace(fromApi.FullName) && !string.IsNullOrWhiteSpace(fromLogin.FullName))
				{
					fromApi.FullName = fromLogin.FullName;
				}

				if (string.IsNullOrWhiteSpace(fromApi.Name) && !string.IsNullOrWhiteSpace(fromLogin.Name))
				{
					fromApi.Name = fromLogin.Name;
				}

				if (string.IsNullOrWhiteSpace(fromApi.SchoolId) && !string.IsNullOrWhiteSpace(fromLogin.SchoolId))
				{
					fromApi.SchoolId = fromLogin.SchoolId;
				}

				if (string.IsNullOrWhiteSpace(fromApi.OrganizationName) && !string.IsNullOrWhiteSpace(fromLogin.OrganizationName))
				{
					fromApi.OrganizationName = fromLogin.OrganizationName;
				}

				if (!HasAnyRoleData(fromApi) && HasAnyRoleData(fromLogin))
				{
					fromApi.UserRole = fromLogin.UserRole;
					fromApi.CurrentRole = fromLogin.CurrentRole;
					fromApi.Roles = fromLogin.Roles;
					fromApi.RoleId = fromLogin.RoleId;
					fromApi.RoleIdCamel = fromLogin.RoleIdCamel;
					fromApi.RoleType = fromLogin.RoleType;
					fromApi.RoleTypeCamel = fromLogin.RoleTypeCamel;
					fromApi.UserRoleSlug = fromLogin.UserRoleSlug;
					fromApi.Type = fromLogin.Type;
					fromApi.AccountType = fromLogin.AccountType;
				}
			}

			return fromApi;
		}

		return fromLogin;
	}

	private static (User? User, string? Token) TryParseLoginSuccess(string json)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;

		User? user;
		try
		{
			user = ApiUserJson.DeserializeUser(root, JsonOptions);
			if (user is null)
			{
				user = JsonSerializer.Deserialize<User>(json, JsonOptions);
			}
		}
		catch (JsonException)
		{
			user = null;
		}

		user ??= LaravelUserCoercion.TryBuild(json, JsonOptions);

		string? token = null;
		if (root.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String)
		{
			token = t.GetString();
		}
		else if (root.TryGetProperty("access_token", out var at) && at.ValueKind == JsonValueKind.String)
		{
			token = at.GetString();
		}
		else if (root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object)
		{
			if (d.TryGetProperty("access_token", out var dat) && dat.ValueKind == JsonValueKind.String)
			{
				token = dat.GetString();
			}
			else if (d.TryGetProperty("token", out var dt) && dt.ValueKind == JsonValueKind.String)
			{
				token = dt.GetString();
			}
		}

		return (user, token);
	}

	private static string? TryParseLaravelErrorMessage(string json)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
			{
				return m.GetString();
			}
		}
		catch (JsonException)
		{
		}

		return null;
	}
}
