using docusystem.Models;

namespace docusystem.Services;

/// <summary>App-wide session: bearer token + user info for the logged-in user (memory + optional secure storage).</summary>
public interface ISessionService
{
	/// <summary>Saves session after a successful API login. Updates in-memory state and <see cref="SessionPersistenceService"/> (SecureStorage).</summary>
	Task SetFromLoginAsync(User user, string? accessToken, CancellationToken cancellationToken = default);

	/// <summary>Sets session from explicit fields. Prefer <see cref="SetFromLoginAsync"/> when you already have a <see cref="User"/>.</summary>
	Task SetSessionAsync(
		string? token,
		int userId,
		string fullName,
		string email,
		string roleType,
		CancellationToken cancellationToken = default);

	User? GetCurrentUser();
	string? GetToken();
	bool IsLoggedIn();
	Task ClearSessionAsync(CancellationToken cancellationToken = default);
}
