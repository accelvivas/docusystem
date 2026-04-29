using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Approval actions and derived workflow display — server is source of truth for routing.
/// </summary>
public interface IApprovalService
{
	/// <summary>Builds a read-only timeline locally when <c>/api/proposals/{id}/workflow</c> is unavailable.</summary>
	IReadOnlyList<ApprovalStep> BuildApprovalSteps(Proposal proposal);

	/// <summary>POST /api/proposals/{id}/approve — signs the current stage on behalf of the user.</summary>
	Task<ApiActionResult> ApproveProposalAsync(int proposalId, CancellationToken cancellationToken = default);

	/// <summary>POST /api/proposals/{id}/return — returns the proposal to the RSO with reviewer remarks.</summary>
	Task<ApiActionResult> ReturnProposalAsync(int proposalId, string? remarks, CancellationToken cancellationToken = default);

	/// <summary>POST /api/proposals/{id}/reject — terminal rejection with reason; only available to allowed roles.</summary>
	Task<ApiActionResult> RejectProposalAsync(int proposalId, string? reason, CancellationToken cancellationToken = default);
}
