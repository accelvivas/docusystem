using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Proposals from Laravel — list/detail/update.
/// </summary>
public interface IProposalService
{
	/// <summary>TODO: GET /api/proposals/pending (or equivalent) — backend filters by authenticated user.</summary>
	Task<IReadOnlyList<Proposal>> GetPendingApprovalsAsync(CancellationToken cancellationToken = default);

	/// <summary>TODO: GET /api/proposals/{id}</summary>
	Task<Proposal?> GetProposalByIdAsync(int proposalId, CancellationToken cancellationToken = default);

	/// <summary>TODO: PUT/PATCH /api/proposals/{id}</summary>
	Task<ApiActionResult> UpdateProposalAsync(Proposal proposal, CancellationToken cancellationToken = default);
}

public sealed class ApiActionResult
{
	public bool Success { get; init; }
	public string? Message { get; init; }

	public static ApiActionResult Ok(string? message = null) => new() { Success = true, Message = message };
	public static ApiActionResult Fail(string message) => new() { Success = false, Message = message };
}
