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

		var effectiveRoles = ResolveEffectiveApproverRoles(user);
		var stages = ProposalWorkflowService.GetStages(proposal.ApprovalFlowType);
		var isSignatoryForThisProposal = stages.Any(stage =>
			effectiveRoles.Any(role => ProposalWorkflowService.IsEquivalentRole(stage, role)));

		if (!isSignatoryForThisProposal)
		{
			proposal.CanEdit = false;
			proposal.CanApprove = false;
			return;
		}

		var atTheirStage = effectiveRoles.Any(role =>
			ProposalWorkflowService.IsEquivalentRole(proposal.CurrentStage, role));
		var canActAsReviewer = atTheirStage && IsActionableStatus(proposal.Status);

		proposal.CanEdit = canActAsReviewer;
		proposal.CanApprove = canActAsReviewer;
	}

	private static IReadOnlyList<string> ResolveEffectiveApproverRoles(User user)
	{
		var roles = new List<string>();
		if (!string.IsNullOrWhiteSpace(user.Role))
		{
			roles.Add(user.Role);
		}

		// Final mobile scope: Admin account can handle both SDAO Assistant and
		// SDAO Coordinator stages.
		if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
		{
			roles.Add("SDAO Assistant");
			roles.Add("SDAO Coordinator");
		}

		// Backward compatibility with existing "SDAO Staff" role naming.
		if (string.Equals(user.Role, "SDAO Staff", StringComparison.OrdinalIgnoreCase))
		{
			roles.Add("SDAO Assistant");
			roles.Add("SDAO Coordinator");
		}

		return roles
			.Where(r => !string.IsNullOrWhiteSpace(r))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
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

		return string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(status, "Under Review", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(status, "Submitted", StringComparison.OrdinalIgnoreCase);
	}
}
