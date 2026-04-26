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

	/// <summary>
	/// When true, the next navigation to <c>proposalform</c> should open the form in browse-only mode (even if the user could edit).
	/// Cleared by <see cref="TryConsumeProposalFormBrowseOnly"/> after the form reads it.
	/// </summary>
	public bool NextProposalFormOpenBrowseOnly { get; private set; }

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

	/// <summary>Call before <c>GoToAsync("proposalform")</c>: <paramref name="browseOnly"/> true from &quot;View&quot;, false from &quot;Edit / Collaborate&quot;.</summary>
	public void PrepareProposalFormNavigation(bool browseOnly)
	{
		NextProposalFormOpenBrowseOnly = browseOnly;
	}

	/// <summary>Returns whether this open was &quot;view only&quot; and resets the flag.</summary>
	public bool TryConsumeProposalFormBrowseOnly()
	{
		var v = NextProposalFormOpenBrowseOnly;
		NextProposalFormOpenBrowseOnly = false;
		return v;
	}

	public void ClearSession()
	{
		CurrentUser = null;
		SelectedProposal = null;
		NextProposalFormOpenBrowseOnly = false;
		AccessToken = null;
	}
}
