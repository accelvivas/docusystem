using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Laravel authentication — TODO: wire to Sanctum/Passport login endpoint.
/// </summary>
public interface IAuthService
{
	/// <summary>
	/// On success, implementation stores the session via <see cref="ISessionService.SetFromLoginAsync"/>
	/// (in-memory + secure storage) so the token is available for <c>LaravelApi</c> and other services.
	/// </summary>
	Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);

	/// <summary>TODO: POST /api/logout and revoke token on server if applicable.</summary>
	Task LogoutAsync(CancellationToken cancellationToken = default);

	/// <summary>TODO: GET /api/user — hydrate current user from token.</summary>
	Task<User?> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}

public sealed class LoginResult
{
	public bool Success { get; init; }
	public string? Message { get; init; }
	public User? User { get; init; }
	public string? AccessToken { get; init; }

	public static LoginResult Ok(User user, string? accessToken) =>
		new() { Success = true, User = user, AccessToken = accessToken };

	public static LoginResult Fail(string message) =>
		new() { Success = false, Message = message };
}
