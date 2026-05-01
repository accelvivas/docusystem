using System.Text.Json.Serialization;
using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Proposal revision / audit log from Laravel.
/// </summary>
public interface IRevisionService
{
	/// <summary>GET proposal action history (tries /history, /revision-history, then /revisions).</summary>
	Task<IReadOnlyList<RevisionLog>> GetRevisionHistoryAsync(int proposalId, CancellationToken cancellationToken = default);

	/// <summary>GET /api/proposals/{proposalId}/field-reviews — current per-field passed/revision states.</summary>
	Task<IReadOnlyList<FieldReviewEntry>> GetFieldReviewsAsync(int proposalId, CancellationToken cancellationToken = default);

	/// <summary>POST /api/proposals/{proposalId}/field-reviews — saves the approver's per-field decisions.</summary>
	Task<ApiActionResult> SubmitFieldChangesAsync(
		int proposalId,
		IReadOnlyList<FieldChange> changes,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Payload for one row inside <c>POST /api/proposals/{id}/field-reviews</c>.
/// Matches the validation rules in <c>ProposalRevisionController::storeFieldReviews</c>.
/// </summary>
public sealed class FieldChange
{
	[JsonPropertyName("field_key")]
	public string FieldKey { get; init; } = string.Empty;

	[JsonPropertyName("field_label")]
	public string FieldLabel { get; init; } = string.Empty;

	/// <summary><c>passed</c> or <c>revision</c>.</summary>
	[JsonPropertyName("status")]
	public string Status { get; init; } = string.Empty;

	/// <summary>Required when <see cref="Status"/> is <c>revision</c>.</summary>
	[JsonPropertyName("comment")]
	public string? Comment { get; init; }
}

/// <summary>
/// Single field-review row returned by <c>GET /api/proposals/{id}/field-reviews</c>.
/// </summary>
public sealed class FieldReviewEntry
{
	[JsonPropertyName("field_key")]
	public string FieldKey { get; init; } = string.Empty;

	[JsonPropertyName("field_label")]
	public string FieldLabel { get; init; } = string.Empty;

	/// <summary><c>passed</c> or <c>revision</c> (lowercase).</summary>
	[JsonPropertyName("status")]
	public string Status { get; init; } = string.Empty;

	[JsonPropertyName("comment")]
	public string? Comment { get; init; }

	[JsonPropertyName("reviewer_name")]
	public string? ReviewerName { get; init; }

	[JsonPropertyName("reviewed_at")]
	public DateTime? ReviewedAt { get; init; }
}
