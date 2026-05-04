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

		var trackingOnly = IsRsoPresident(currentUser);
		var proposals = trackingOnly
			? (await _proposalService.GetMySubmissionsAsync()).ToList()
			: (await _proposalService.GetPendingApprovalsAsync()).ToList();
		var weekStart = DateTime.Today.AddDays(-7);

		// Use the same normalized status strings as the rest of the app (see Proposal.NormalizeStatus).
		// Pending Approvals API often returns "Pending", not only "Under Review" / "Submitted" — the
		// dashboard used to count only those two, so it showed "No urgent items" while the list had rows.
		var needsMyReview = trackingOnly
			? proposals.Count(p => IsRsoRoutingInProgressStatus(p))
			: proposals.Count(p => IsApproverPendingQueueAttention(p));

		var revisionFollowUp = proposals.Count(p =>
			string.Equals(NormalizeStatus(p), "Returned for Revision", StringComparison.OrdinalIgnoreCase));

		var approvedThisWeek = proposals.Count(p =>
		{
			var s = NormalizeStatus(p);
			return (string.Equals(s, "Fully Approved", StringComparison.OrdinalIgnoreCase) ||
			        string.Equals(s, "Approved", StringComparison.OrdinalIgnoreCase)) &&
			       (p.FullyApprovedAt ?? p.SubmittedDate) >= weekStart;
		});

		var rejectedOrReturnedThisWeek = proposals.Count(p =>
		{
			var s = NormalizeStatus(p);
			return (string.Equals(s, "Rejected", StringComparison.OrdinalIgnoreCase) ||
			        string.Equals(s, "Returned for Revision", StringComparison.OrdinalIgnoreCase)) &&
			       p.SubmittedDate >= weekStart;
		});

		var overdueItems = proposals.Count(p =>
		{
			var s = NormalizeStatus(p);
			if (string.Equals(s, "Returned for Revision", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(s, "Rejected", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(s, "Fully Approved", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			return IsActiveRoutingStatus(s) &&
			       p.SubmittedDate != default &&
			       p.SubmittedDate < DateTime.Today.AddDays(-7);
		});

		NeedsAttentionLabel.Text = BuildNeedsAttentionText(
			currentUser.Role,
			needsMyReview,
			revisionFollowUp,
			approvedThisWeek,
			overdueItems);
	}

	private void ApplyHeader(User u)
	{
		UserNameLabel.Text = u.DisplayName;
		UserRoleLabel.Text = string.IsNullOrWhiteSpace(u.Role) ? "—" : u.Role;
		ResponsibilityHintLabel.Text = BuildResponsibilityLine(u.Role);
	}

	private static string BuildResponsibilityLine(string role)
	{
		return IsRsoPresident(role)
			? "Track your submitted proposals and monitor their current routing stage."
			: "Review proposals when they reach your stage (Pending Approvals).";
	}

	private static string BuildNeedsAttentionText(
		string role,
		int needsMyReview,
		int revisionFollowUp,
		int approvedThisWeek,
		int overdueItems)
	{
		if (IsRsoPresident(role))
		{
			var trackingLines = new List<string>();
			if (needsMyReview > 0)
			{
				trackingLines.Add($"• {needsMyReview} of your submissions are currently in routing/review.");
			}
			if (revisionFollowUp > 0)
			{
				trackingLines.Add($"• {revisionFollowUp} of your submissions were returned for revision.");
			}
			if (approvedThisWeek > 0)
			{
				trackingLines.Add($"• {approvedThisWeek} of your submissions were approved this week.");
			}
			if (overdueItems > 0)
			{
				trackingLines.Add($"• {overdueItems} of your submissions are waiting longer than expected.");
			}

			return trackingLines.Count == 0
				? "No urgent updates. Open Pending Approvals to monitor your submitted proposals."
				: string.Join('\n', trackingLines);
		}

		var lines = new List<string>();
		if (needsMyReview > 0)
		{
			lines.Add($"• {needsMyReview} need your review (Pending Approvals).");
		}

		if (revisionFollowUp > 0)
		{
			lines.Add($"• {revisionFollowUp} are for revision follow-up.");
		}

		if (overdueItems > 0)
		{
			lines.Add($"• {overdueItems} are overdue and need attention.");
		}

		if (approvedThisWeek > 0)
		{
			lines.Add($"• {approvedThisWeek} approved this week.");
		}

		if (lines.Count == 0)
		{
			return "No urgent items. Open Pending Approvals to browse.";
		}

		return string.Join('\n', lines);
	}

	private static bool IsRsoPresident(User? user)
	{
		if (user is null)
		{
			return false;
		}

		return IsRsoPresident(user.Role) ||
		       string.Equals(user.RoleKey, "rso_president", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(user.RoleKey, "org_officer", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsRsoPresident(string? role) =>
		!string.IsNullOrWhiteSpace(role) &&
		(string.Equals(role, "RSO President", StringComparison.OrdinalIgnoreCase) ||
		 string.Equals(role, "Organization Officer", StringComparison.OrdinalIgnoreCase));

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

	private static string NormalizeStatus(Proposal p) =>
		Proposal.NormalizeStatus(string.IsNullOrWhiteSpace(p.Status) ? null : p.Status);

	/// <summary>Statuses that mean the proposal is still moving through review (not terminal, not returned).</summary>
	private static bool IsActiveRoutingStatus(string s) =>
		string.Equals(s, "Pending", StringComparison.OrdinalIgnoreCase) ||
		string.Equals(s, "Under Review", StringComparison.OrdinalIgnoreCase) ||
		string.Equals(s, "Submitted", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Approver pending queue: anything still actionable counts as needing attention except
	/// returned / rejected / fully done. Unknown non-terminal labels still count (same as list presence).
	/// </summary>
	private static bool IsApproverPendingQueueAttention(Proposal p)
	{
		var s = NormalizeStatus(p);
		if (string.Equals(s, "Returned for Revision", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(s, "Rejected", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(s, "Fully Approved", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		return true;
	}

	/// <summary>RSO "my submissions" — in-flight routing/review only (not final states).</summary>
	private static bool IsRsoRoutingInProgressStatus(Proposal p)
	{
		var s = NormalizeStatus(p);
		return IsActiveRoutingStatus(s);
	}
}
