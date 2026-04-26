using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Proposal revision / audit log from Laravel.
/// </summary>
public interface IRevisionService
{
	/// <summary>TODO: GET /api/proposals/{proposalId}/revisions (adjust route to your API).</summary>
	Task<IReadOnlyList<RevisionLog>> GetRevisionHistoryAsync(int proposalId, CancellationToken cancellationToken = default);

	/// <summary>
	/// TODO: Optional — POST field-level change log if your API records client-side edits separately.
	/// </summary>
	Task<ApiActionResult> SubmitFieldChangesAsync(
		int proposalId,
		IReadOnlyList<FieldChange> changes,
		CancellationToken cancellationToken = default);
}

public sealed class FieldChange
{
	public string FieldName { get; init; } = string.Empty;
	public string? OldValue { get; init; }
	public string? NewValue { get; init; }
}
