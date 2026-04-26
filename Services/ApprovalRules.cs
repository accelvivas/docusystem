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

		var stages = ProposalWorkflowService.GetStages(proposal.ApprovalFlowType);
		var isSignatoryForThisProposal = stages.Any(s =>
			string.Equals(s, user.Role, StringComparison.OrdinalIgnoreCase));

		if (!isSignatoryForThisProposal)
		{
			proposal.CanEdit = false;
			proposal.CanApprove = false;
			return;
		}

		var atTheirStage = string.Equals(proposal.CurrentStage, user.Role, StringComparison.OrdinalIgnoreCase);
		var canActAsReviewer = atTheirStage &&
			(string.Equals(proposal.Status, "Under Review", StringComparison.OrdinalIgnoreCase) ||
			 string.Equals(proposal.Status, "Submitted", StringComparison.OrdinalIgnoreCase));

		proposal.CanEdit = canActAsReviewer;
		proposal.CanApprove = canActAsReviewer;
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
}
