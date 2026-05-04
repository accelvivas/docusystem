using System.Linq;
using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Client-side permission checks for proposals. Prefer server-provided
/// <see cref="Proposal.CanEdit"/> / <see cref="Proposal.CanApprove"/> when present; this fills in
/// flags from role and workflow stage when needed for the UI.
/// </summary>
public static class ApprovalRules
{
	/// <summary>
	/// Collects labels/slugs that should match <see cref="ProposalWorkflowService"/> stage names.
	/// Uses <see cref="User.Role"/>, <see cref="User.RoleId"/> (catalog), and <see cref="User.RoleKey"/>.
	/// </summary>
	public static IReadOnlyList<string> GetReviewerRoleHints(User user)
	{
		var roles = new List<string>();

		if (!string.IsNullOrWhiteSpace(user.Role))
		{
			roles.Add(user.Role.Trim());
		}

		var rid = user.RoleId ?? user.RoleIdCamel;
		if (rid is int id && id > 0 && RoleIdCatalog.TryGetDisplayName(id, out var catalogLabel))
		{
			roles.Add(catalogLabel);
		}

		AddWorkflowHintsFromRoleKey(user.RoleKey, roles);

		var distinct = roles
			.Where(r => !string.IsNullOrWhiteSpace(r))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		var rk = user.RoleKey?.Trim().ToLowerInvariant();
		var adminLike = distinct.Any(r => string.Equals(r, "Admin", StringComparison.OrdinalIgnoreCase)) ||
		                rid == 8 ||
		                rk == "admin";
		var sdaoLike = distinct.Any(r => string.Equals(r, "SDAO Staff", StringComparison.OrdinalIgnoreCase)) ||
		               rid == 7 ||
		               rk == "sdao_staff";

		if (adminLike || sdaoLike)
		{
			distinct.Add("SDAO Assistant");
			distinct.Add("SDAO Coordinator");
		}

		return distinct
			.Where(r => !string.IsNullOrWhiteSpace(r))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static void AddWorkflowHintsFromRoleKey(string? roleKey, List<string> roles)
	{
		if (string.IsNullOrWhiteSpace(roleKey))
		{
			return;
		}

		switch (roleKey.Trim().ToLowerInvariant())
		{
			case "adviser":
			case "advisor":
				roles.Add("Adviser");
				return;
			case "program_chair":
				roles.Add("Program Chair");
				return;
			case "dean":
				roles.Add("Dean");
				return;
			case "academic_director":
				roles.Add("Academic Director");
				return;
			case "executive_director":
				roles.Add("Executive Director");
				return;
			case "sdao_staff":
				roles.Add("SDAO Staff");
				return;
			case "admin":
				roles.Add("Admin");
				return;
			case "rso_president":
				roles.Add("RSO President");
				return;
			case "student":
				roles.Add("Student");
				return;
		}
	}

	/// <summary>
	/// Sets <see cref="Proposal.CanEdit"/> and <see cref="Proposal.CanApprove"/> from the current user, role, and stage.
	/// </summary>
	public static void ApplyWorkflowPermissions(Proposal proposal, User? user)
	{
		if (user is null)
		{
			proposal.CanEdit = false;
			proposal.CanApprove = false;
			return;
		}

		if (string.Equals(user.Role, "RSO President", StringComparison.OrdinalIgnoreCase) &&
		    (string.IsNullOrWhiteSpace(user.OrganizationName) ||
		     !string.Equals(proposal.OrganizationName, user.OrganizationName, StringComparison.OrdinalIgnoreCase)))
		{
			proposal.CanEdit = false;
			proposal.CanApprove = false;
			return;
		}

		if (string.Equals(proposal.Status, "Fully Approved", StringComparison.OrdinalIgnoreCase))
		{
			proposal.CanEdit = false;
			proposal.CanApprove = false;
			return;
		}

		if (string.Equals(proposal.Status, "Returned for Revision", StringComparison.OrdinalIgnoreCase))
		{
			var isPresident = string.Equals(user.Role, "RSO President", StringComparison.OrdinalIgnoreCase);
			proposal.CanEdit = isPresident;
			proposal.CanApprove = false;
			return;
		}

		if (string.Equals(user.Role, "RSO President", StringComparison.OrdinalIgnoreCase))
		{
			proposal.CanEdit = true;
			proposal.CanApprove = false;
			return;
		}

		// Laravel may send values other than "approver"; blocking unknown strings incorrectly hid Passed/Revision.
		// Only exclude accounts explicitly tagged as students / submitters without workflow duties.
		if (!string.IsNullOrWhiteSpace(user.EffectiveRoleType) &&
		    IsEffectiveRoleTypeExplicitNonApprover(user.EffectiveRoleType))
		{
			proposal.CanEdit = false;
			proposal.CanApprove = false;
			return;
		}

		var effectiveRoles = GetReviewerRoleHints(user);
		var stages = ProposalWorkflowService.GetStages(proposal.ApprovalFlowType);
		var isSignatoryForThisProposal = stages.Any(stage =>
			effectiveRoles.Any(role => ProposalWorkflowService.IsEquivalentRole(stage, role)));

		if (!isSignatoryForThisProposal)
		{
			proposal.CanEdit = false;
			proposal.CanApprove = false;
			return;
		}

		// Preserve Laravel policy output when present — mobile stage strings can be missing
		// (e.g. only current_approval_step in extra) or renamed vs the local workflow list, which
		// would incorrectly hide Passed/Revision for a user who already has the proposal in their queue.
		var serverCanApprove = proposal.CanApprove;

		var atTheirStage = effectiveRoles.Any(role =>
			ProposalWorkflowService.IsEquivalentRole(proposal.CurrentStage, role));
		var actionable = IsActionableStatus(proposal.Status);
		var canActAsReviewer = (atTheirStage && actionable) ||
		                       (serverCanApprove && actionable);

		proposal.CanEdit = canActAsReviewer;
		proposal.CanApprove = canActAsReviewer;
	}

	private static bool IsEffectiveRoleTypeExplicitNonApprover(string raw)
	{
		var t = raw.Trim().ToLowerInvariant()
			.Replace("_", string.Empty, StringComparison.Ordinal)
			.Replace("-", string.Empty, StringComparison.Ordinal)
			.Replace(" ", string.Empty, StringComparison.Ordinal);

		return t is "student" or "submitteronly" or "submitter";
	}

	/// <summary>Uses flags set by the API or <see cref="ApplyWorkflowPermissions"/>.</summary>
	public static bool CanEdit(User? user, Proposal? proposal)
	{
		if (user is null || proposal is null)
		{
			return false;
		}

		if (string.Equals(proposal.Status, "Fully Approved", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		return proposal.CanEdit;
	}

	public static bool CanApprove(User? user, Proposal? proposal) =>
		user is not null && proposal is not null && proposal.CanApprove;

	// Statuses where the current-stage signatory is allowed to act on the proposal.
	// Backend normalizes lowercase "pending" to "Pending" via Proposal.NormalizeStatus, and the
	// approval queue endpoint ships items in that state, so we accept it alongside the
	// older "Under Review" / "Submitted" labels.
	private static bool IsActionableStatus(string? status)
	{
		if (string.IsNullOrWhiteSpace(status))
		{
			return false;
		}

		var normalized = Proposal.NormalizeStatus(status);
		if (string.Equals(normalized, "Pending", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(normalized, "Under Review", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(normalized, "Submitted", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		// Some APIs still emit non-canonical review labels; treat these as actionable.
		var key = status.Trim().ToLowerInvariant()
			.Replace("_", string.Empty, StringComparison.Ordinal)
			.Replace("-", string.Empty, StringComparison.Ordinal)
			.Replace(" ", string.Empty, StringComparison.Ordinal);

		return key is "pendingreview" or "forreview" or "inreview" or "onreview" or "review";
	}
}
