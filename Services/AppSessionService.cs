using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// In-memory session for the signed-in user and navigation context (selected proposal).
/// Token and user snapshot are persisted via <see cref="SessionPersistenceService"/>.
/// </summary>
public sealed class AppSessionService
{
	public User? CurrentUser { get; private set; }
	public Proposal? SelectedProposal { get; private set; }

	/// <summary>Bearer token from Laravel Sanctum / Passport — attached by <see cref="LaravelAuthDelegatingHandler"/>.</summary>
	public string? AccessToken { get; private set; }

	public void SetCurrentUser(User user, string? accessToken = null)
	{
		CurrentUser = user;
		AccessToken = accessToken;
	}

	/// <summary>Sets only the bearer token (e.g. after loading from <see cref="Microsoft.Maui.Storage.SecureStorage"/>). Prefer <see cref="SetCurrentUser"/> when the user object is known.</summary>
	public void SetAccessToken(string? accessToken) => AccessToken = accessToken;

	public void SetSelectedProposal(Proposal proposal)
	{
		SelectedProposal = proposal;
	}

	public void ClearSession()
	{
		CurrentUser = null;
		SelectedProposal = null;
		AccessToken = null;
	}
}
