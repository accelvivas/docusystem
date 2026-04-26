using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Coordinates the logged-in <see cref="User"/> and bearer token. Uses
/// <see cref="AppSessionService"/> for in-memory state (HttpClient, pages) and
/// <see cref="SessionPersistenceService"/> so the session can survive a cold start.
/// </summary>
/// <remarks>
/// TODO: If you add extra-sensitive fields, consider encrypting values before
/// <see cref="SessionPersistenceService.SaveAsync"/>, or split token vs profile in SecureStorage.
/// </remarks>
public sealed class SessionService : ISessionService
{
	private readonly AppSessionService _appSession;
	private readonly SessionPersistenceService _persistence;

	public SessionService(AppSessionService appSession, SessionPersistenceService persistence)
	{
		_appSession = appSession;
		_persistence = persistence;
	}

	/// <inheritdoc />
	public async Task SetFromLoginAsync(User user, string? accessToken, CancellationToken cancellationToken = default)
	{
		_appSession.SetCurrentUser(user, accessToken);
		await _persistence.SaveAsync(user, accessToken, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public Task SetSessionAsync(
		string? token,
		int userId,
		string fullName,
		string email,
		string roleType,
		CancellationToken cancellationToken = default)
	{
		var user = new User
		{
			Id = userId,
			FullName = fullName,
			Email = email,
			UserRole = string.IsNullOrWhiteSpace(roleType)
				? null
				: new UserRole { Name = roleType.Trim(), DisplayName = roleType.Trim() }
		};

		return SetFromLoginAsync(user, token, cancellationToken);
	}

	/// <inheritdoc />
	public User? GetCurrentUser() => _appSession.CurrentUser;

	/// <inheritdoc />
	public string? GetToken() => _appSession.AccessToken;

	/// <inheritdoc />
	public bool IsLoggedIn() =>
		!string.IsNullOrEmpty(_appSession.AccessToken) &&
		_appSession.CurrentUser is { Email: var e } &&
		!string.IsNullOrWhiteSpace(e);

	/// <inheritdoc />
	public async Task ClearSessionAsync(CancellationToken cancellationToken = default)
	{
		_appSession.ClearSession();
		await _persistence.ClearAsync(cancellationToken).ConfigureAwait(false);
	}
}
