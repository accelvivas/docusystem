using System.Text.Json.Serialization;

namespace docusystem.Models;

/// <summary>
/// One row from the server-driven approval timeline. Field names line up 1:1 with
/// <c>ProposalController::workflow</c> / <c>workflow_summary</c> in <c>show</c> responses
/// (i.e. <c>step_id</c>, <c>step_order</c>, <c>role_name</c>, <c>status</c>,
/// <c>is_current_step</c>, <c>assigned_to</c>, <c>review_comments</c>, <c>acted_at</c>).
/// </summary>
public class ApprovalStep
{
	[JsonPropertyName("step_id")]
	public int Id { get; set; }

	[JsonPropertyName("step_order")]
	public int StepNumber { get; set; }

	[JsonPropertyName("role_name")]
	public string RoleName { get; set; } = string.Empty;

	[JsonPropertyName("status")]
	public string Status { get; set; } = string.Empty;

	[JsonPropertyName("is_current_step")]
	public bool IsCurrentStep { get; set; }

	/// <summary>API: <c>assigned_to</c> — full name of the assigned approver, or null.</summary>
	[JsonPropertyName("assigned_to")]
	public string? ReviewedBy { get; set; }

	/// <summary>API: <c>review_comments</c> — note left by the approver on this step.</summary>
	[JsonPropertyName("review_comments")]
	public string? ReviewComments { get; set; }

	/// <summary>API: <c>acted_at</c> — when this step was acted on (approved, returned, etc.).</summary>
	[JsonPropertyName("acted_at")]
	public DateTime? ActedAt { get; set; }
}
