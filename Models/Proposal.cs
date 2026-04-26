using System.Text.Json.Serialization;

namespace docusystem.Models;

/// <summary>
/// Proposal summary/detail — align property names with Laravel API JSON (snake_case typical).
/// </summary>
public class Proposal
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[JsonPropertyName("organization_name")]
	public string OrganizationName { get; set; } = string.Empty;

	[JsonPropertyName("submitted_by")]
	public string SubmittedBy { get; set; } = string.Empty;

	[JsonPropertyName("current_stage")]
	public string CurrentStage { get; set; } = string.Empty;

	[JsonPropertyName("status")]
	public string Status { get; set; } = string.Empty;

	[JsonPropertyName("activity_date")]
	public DateTime ActivityDate { get; set; }

	[JsonPropertyName("venue")]
	public string Venue { get; set; } = string.Empty;

	[JsonPropertyName("budget")]
	public decimal Budget { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; } = string.Empty;

	/// <summary>Server-computed: whether the current user may edit this proposal.</summary>
	[JsonPropertyName("can_edit")]
	public bool CanEdit { get; set; }

	/// <summary>Server-computed: whether the current user may approve at this stage.</summary>
	[JsonPropertyName("can_approve")]
	public bool CanApprove { get; set; }

	[JsonPropertyName("submitted_date")]
	public DateTime SubmittedDate { get; set; }

	/// <summary>Optional — set when status is fully approved (Laravel timestamp).</summary>
	[JsonPropertyName("fully_approved_at")]
	public DateTime? FullyApprovedAt { get; set; }

	/// <summary>Latest return-for-revision remarks from the reviewer (align with API field when available).</summary>
	[JsonIgnore]
	public string? LastRemarks { get; set; }

	/// <summary>
	/// Whether this proposal follows the Academic or Non-Academic signatory chain (map from API when available).
	/// </summary>
	[JsonIgnore]
	public ApprovalFlowType ApprovalFlowType { get; set; } = ApprovalFlowType.Academic;
}
