namespace docusystem.Pages.Dashboard;

using docusystem.Models;
using docusystem.Services;

/// <summary>
/// Dashboard — counts from <see cref="IProposalService"/> (empty until API is wired).
/// </summary>
public partial class DashboardPage : ContentPage
{
	private readonly AppSessionService _session;
	private readonly IProposalService _proposalService;
	private readonly IAuthService _authService;

	public DashboardPage(
		AppSessionService session,
		IProposalService proposalService,
		IAuthService authService)
	{
		InitializeComponent();
		_session = session;
		_proposalService = proposalService;
		_authService = authService;

		// Fills the header from session before the first network refresh so the UI does not flash "User Name".
		if (session.CurrentUser is not null)
		{
			ApplyHeader(session.CurrentUser);
		}
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		var currentUser = _session.CurrentUser;
		if (currentUser is null)
		{
			await Shell.Current.GoToAsync("//login");
			return;
		}

		// Always hydrates from GET /api/user (Laravel) so role/role_type is current; updates session in AuthService.
		await _authService.GetCurrentUserAsync();
		currentUser = _session.CurrentUser;
		if (currentUser is null)
		{
			await Shell.Current.GoToAsync("//login");
			return;
		}

		UserNameLabel.Text = currentUser.DisplayName;
		// Role comes from API role.display_name / role.name (see User.Role → UserRole).
		UserRoleLabel.Text = string.IsNullOrWhiteSpace(currentUser.Role)
			? "—"
			: currentUser.Role;
		ResponsibilityHintLabel.Text = BuildResponsibilityLine(currentUser.Role);

		var proposals = (await _proposalService.GetPendingApprovalsAsync()).ToList();

		var needsMyReview = proposals.Count(p =>
			string.Equals(p.Status, "Under Review", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(p.Status, "Submitted", StringComparison.OrdinalIgnoreCase));

		var returned = proposals.Count(p =>
			string.Equals(p.Status, "Returned for Revision", StringComparison.OrdinalIgnoreCase));

		var waitingOnOthers = proposals.Count(p =>
			(string.Equals(p.Status, "Under Review", StringComparison.OrdinalIgnoreCase) ||
			 string.Equals(p.Status, "Submitted", StringComparison.OrdinalIgnoreCase)) &&
			!string.Equals(p.CurrentStage, currentUser.Role, StringComparison.OrdinalIgnoreCase));

		var approved = proposals.Count(p =>
			string.Equals(p.Status, "Fully Approved", StringComparison.OrdinalIgnoreCase));

		PendingCountLabel.Text = needsMyReview.ToString();
		ReturnedCountLabel.Text = returned.ToString();
		WaitingOthersCountLabel.Text = Math.Max(0, waitingOnOthers).ToString();
		ApprovedCountLabel.Text = approved.ToString();

		NeedsAttentionLabel.Text = BuildNeedsAttentionText(
			currentUser.Role,
			needsMyReview,
			returned,
			waitingOnOthers,
			approved);
	}

	private void ApplyHeader(User u)
	{
		UserNameLabel.Text = u.DisplayName;
		UserRoleLabel.Text = string.IsNullOrWhiteSpace(u.Role) ? "—" : u.Role;
		ResponsibilityHintLabel.Text = BuildResponsibilityLine(u.Role);
	}

	private static string BuildResponsibilityLine(string role)
	{
		if (string.Equals(role, "RSO President", StringComparison.OrdinalIgnoreCase))
		{
			return "Submit and update proposals; reviewers sign off by stage.";
		}

		return "Review proposals when they reach your stage (Pending Approvals).";
	}

	private static string BuildNeedsAttentionText(
		string role,
		int needsMyReview,
		int returned,
		int waitingOnOthers,
		int approved)
	{
		var lines = new List<string>();
		if (needsMyReview > 0)
		{
			lines.Add($"• {needsMyReview} need your review (Pending Approvals).");
		}

		if (returned > 0)
		{
			lines.Add($"• {returned} returned for revision — open details for remarks.");
		}

		if (waitingOnOthers > 0)
		{
			lines.Add($"• {waitingOnOthers} with another reviewer.");
		}

		if (approved > 0)
		{
			lines.Add($"• {approved} fully approved.");
		}

		if (lines.Count == 0)
		{
			return "No urgent items. Open Pending Approvals to browse.";
		}

		return string.Join('\n', lines);
	}

	/// <summary>Dashboard → full Pending Approvals list (All).</summary>
	private async void OnGoToPendingApprovalsBrowseClicked(object? sender, EventArgs e) =>
		await GoToPendingApprovalsWithFilterAsync("all");

	/// <summary>Summary card: items at the user’s stage (Needs my review).</summary>
	private async void OnOpenPendingNeedsReviewClicked(object? sender, EventArgs e) =>
		await GoToPendingApprovalsWithFilterAsync("pending");

	private async void OnViewReturnedClicked(object? sender, EventArgs e) =>
		await GoToPendingApprovalsWithFilterAsync("returned");

	/// <summary>Completed count card — same queue with All filter (fully approved may be off-queue depending on API).</summary>
	private async void OnViewApprovedClicked(object? sender, EventArgs e) =>
		await GoToPendingApprovalsWithFilterAsync("all");

	private static async Task GoToPendingApprovalsWithFilterAsync(string filter)
	{
		var safe = Uri.EscapeDataString(filter);
		await Shell.Current.GoToAsync($"//pendingapprovals?filter={safe}");
	}
}
