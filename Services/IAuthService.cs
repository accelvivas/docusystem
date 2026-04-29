using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Laravel authentication — TODO: wire to Sanctum/Passport login endpoint.
/// </summary>
public interface IAuthService
{
	/// <summary>
	/// POST /api/login (Sanctum). On success, implementation stores the session via
	/// <see cref="ISessionService.SetFromLoginAsync"/> (in-memory + secure storage) so the
	/// token is available for <c>LaravelApi</c> and other services.
	/// </summary>
	Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);

	/// <summary>
	/// POST /api/register — creates an account on the Laravel backend. On success, the same
	/// session pipeline as <see cref="LoginAsync"/> is used so the user is signed in immediately.
	/// </summary>
	Task<LoginResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

	/// <summary>POST /api/logout — revokes the bearer token on the server (best-effort).</summary>
	Task LogoutAsync(CancellationToken cancellationToken = default);

	/// <summary>GET /api/user — hydrates the current user from the active token.</summary>
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

/// <summary>Payload for <see cref="IAuthService.RegisterAsync"/> — sent to <c>POST /api/register</c>.</summary>
public sealed class RegisterRequest
{
	public string Email { get; init; } = string.Empty;
	public string Password { get; init; } = string.Empty;
	public string? PasswordConfirmation { get; init; }
	public string? FirstName { get; init; }
	public string? LastName { get; init; }
	public string? Name { get; init; }
	public string? SchoolId { get; init; }
	public string? OrganizationName { get; init; }
}
