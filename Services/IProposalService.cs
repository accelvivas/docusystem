using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Proposals from Laravel — list/detail/update.
/// </summary>
public interface IProposalService
{
	/// <summary>GET /api/approvals/pending — backend filters by authenticated approver/role.</summary>
	Task<IReadOnlyList<Proposal>> GetPendingApprovalsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// GET proposals owned by the logged-in submitter (e.g. RSO President "my submissions" queue).
	/// The implementation may probe multiple API paths depending on backend route naming.
	/// </summary>
	Task<IReadOnlyList<Proposal>> GetMySubmissionsAsync(CancellationToken cancellationToken = default);

	/// <summary>GET /api/proposals/{id} — full proposal payload (incl. nested fields when available).</summary>
	Task<Proposal?> GetProposalByIdAsync(int proposalId, CancellationToken cancellationToken = default);

	/// <summary>GET /api/proposals/{id}/workflow — server-driven approval timeline (preferred over local build).</summary>
	Task<IReadOnlyList<ApprovalStep>> GetProposalWorkflowAsync(int proposalId, CancellationToken cancellationToken = default);

	/// <summary>PUT /api/proposals/{id} — kept for legacy edits; new flow uses field-reviews.</summary>
	Task<ApiActionResult> UpdateProposalAsync(Proposal proposal, CancellationToken cancellationToken = default);
}

public sealed class ApiActionResult
{
	public bool Success { get; init; }
	public string? Message { get; init; }

	public static ApiActionResult Ok(string? message = null) => new() { Success = true, Message = message };
	public static ApiActionResult Fail(string message) => new() { Success = false, Message = message };
}
