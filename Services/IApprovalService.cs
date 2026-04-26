using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Approval actions and derived workflow display — server is source of truth for routing.
/// </summary>
public interface IApprovalService
{
	/// <summary>Builds a read-only timeline from proposal fields returned by the API.</summary>
	IReadOnlyList<ApprovalStep> BuildApprovalSteps(Proposal proposal);

	/// <summary>TODO: POST /api/proposals/{id}/approve (Laravel route as designed).</summary>
	Task<ApiActionResult> ApproveProposalAsync(int proposalId, CancellationToken cancellationToken = default);

	/// <summary>TODO: POST /api/proposals/{id}/return-for-revision with remarks body.</summary>
	Task<ApiActionResult> ReturnProposalAsync(int proposalId, string? remarks, CancellationToken cancellationToken = default);
}
