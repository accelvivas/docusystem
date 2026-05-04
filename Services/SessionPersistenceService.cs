using System.Text.Json;
using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Persists bearer token and user JSON in <see cref="Microsoft.Maui.Storage.SecureStorage"/> (OS keychain / encrypted prefs).
/// Called from <see cref="ISessionService"/> after login — <see cref="Maui.MauiApp"/> can restore on next launch.
/// </summary>
public sealed class SessionPersistenceService
{
	private const string KeyToken = "docusystem_access_token";
	private const string KeyUserJson = "docusystem_user_json";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		WriteIndented = false
	};

	public async Task SaveAsync(User user, string? accessToken, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(accessToken))
		{
			await ClearAsync(cancellationToken).ConfigureAwait(false);
			return;
		}

		var json = JsonSerializer.Serialize(user, JsonOptions);
		await SecureStorage.SetAsync(KeyToken, accessToken).WaitAsync(cancellationToken).ConfigureAwait(false);
		await SecureStorage.SetAsync(KeyUserJson, json).WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	public Task ClearAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			SecureStorage.Remove(KeyToken);
			SecureStorage.Remove(KeyUserJson);
		}
		catch
		{
			// Best-effort clear.
		}

		return Task.CompletedTask;
	}

	/// <summary>Restores session and navigates to dashboard when valid stored credentials exist.</summary>
	public async Task TryRestoreAsync(AppSessionService session, AppShell shell, CancellationToken cancellationToken = default)
	{
		string? token;
		string? userJson;
		try
		{
			token = await SecureStorage.GetAsync(KeyToken).WaitAsync(cancellationToken).ConfigureAwait(false);
			userJson = await SecureStorage.GetAsync(KeyUserJson).WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(userJson))
		{
			return;
		}

		User? user;
		try
		{
			user = JsonSerializer.Deserialize<User>(userJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		}
		catch
		{
			await ClearAsync(cancellationToken).ConfigureAwait(false);
			return;
		}

		if (user is null || string.IsNullOrWhiteSpace(user.Email))
		{
			await ClearAsync(cancellationToken).ConfigureAwait(false);
			return;
		}

		user.NormalizeNestedRoleFromForeignKey();
		session.SetCurrentUser(user, token);

		await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			shell.SetAuthenticatedState(true);
			try
			{
				await shell.GoToAsync("//dashboard");
			}
			catch
			{
				// Navigation can race during shell startup; session is still valid.
			}
		}).ConfigureAwait(false);
	}
}
