using System.Text.Json.Serialization;
using System.Linq;

namespace docusystem.Models;

/// <summary>
/// Unified proposal action/revision history row used by the mobile approver app.
/// Supports both legacy "revisions" payloads and the newer "history" payload shape.
/// </summary>
public class RevisionLog
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("action_type")]
	public string? ActionType { get; set; }

	[JsonPropertyName("proposal_id")]
	public int ProposalId { get; set; }

	[JsonPropertyName("proposal_title")]
	public string? ProposalTitle { get; set; }

	[JsonPropertyName("organization_name")]
	public string? OrganizationName { get; set; }

	[JsonPropertyName("actor_name")]
	public string? ActorName { get; set; }

	[JsonPropertyName("actor_role")]
	public string? ActorRole { get; set; }

	[JsonPropertyName("stage_name")]
	public string? StageName { get; set; }

	[JsonPropertyName("remark")]
	public string? Remark { get; set; }

	[JsonPropertyName("reviewer_comment")]
	public string? ReviewerComment { get; set; }

	[JsonPropertyName("affected_fields")]
	public string[]? AffectedFields { get; set; }

	[JsonPropertyName("current_status_after_action")]
	public string? CurrentStatusAfterAction { get; set; }

	[JsonPropertyName("status_after_action")]
	public string? StatusAfterAction { get; set; }

	// Legacy keys still returned by older /revisions endpoints.
	[JsonPropertyName("type")]
	public string Type { get; set; } = string.Empty;

	[JsonPropertyName("status")]
	public string Status { get; set; } = string.Empty;

	[JsonPropertyName("field_key")]
	public string? FieldKey { get; set; }

	/// <summary>Display label for the field/step (e.g. <c>Title of Activity</c> or <c>Adviser</c>).</summary>
	[JsonPropertyName("field_label")]
	public string FieldLabel { get; set; } = string.Empty;

	/// <summary>The reviewer's revision note / step remarks.</summary>
	[JsonPropertyName("comment")]
	public string? Comment { get; set; }

	[JsonPropertyName("reviewer_name")]
	public string? ReviewerName { get; set; }

	[JsonPropertyName("created_at")]
	public DateTime Timestamp { get; set; }

	[JsonPropertyName("acted_at")]
	public DateTime? ActedAt { get; set; }

	// ───────────────────────────────────────────────────────────────────────
	// UI helpers
	// ───────────────────────────────────────────────────────────────────────

	[JsonIgnore]
	public string FieldChanged => string.IsNullOrWhiteSpace(FieldLabel) ? "Record" : FieldLabel;

	[JsonIgnore]
	public string EditedBy => string.IsNullOrWhiteSpace(ReviewerName) ? "—" : ReviewerName!;

	[JsonIgnore]
	public string Role => Type switch
	{
		"field_revision" => "Field-level revision",
		"workflow_revision" => "Workflow revision",
		"workflow_step_revision" => "Workflow step revision",
		_ => string.IsNullOrWhiteSpace(Type) ? "Revision" : Type
	};

	[JsonIgnore]
	public string OldValue => string.Empty;

	[JsonIgnore]
	public string NewValue => Comment ?? string.Empty;

	[JsonIgnore]
	public string EffectiveActionType
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(ActionType))
			{
				return ActionType!.Trim().ToLowerInvariant();
			}

			var status = (Status ?? string.Empty).Trim().ToLowerInvariant();
			var type = (Type ?? string.Empty).Trim().ToLowerInvariant();
			if (status is "submitted")
			{
				return "submitted";
			}
			if (status is "rejected")
			{
				return "rejected";
			}
			if (status is "approved" or "completed")
			{
				return "approved";
			}
			if (status is "revision_required" or "returned_for_revision")
			{
				return "returned_for_revision";
			}
			if (status is "pending_next_approval" or "forwarded")
			{
				return "stage_forwarded";
			}
			if (type is "field_revision")
			{
				return "remark_added";
			}
			if (type.Contains("workflow", StringComparison.Ordinal))
			{
				return "stage_forwarded";
			}

			return "status_updated";
		}
	}

	[JsonIgnore]
	public string DisplayTitle => EffectiveActionType switch
	{
		"submitted" => "Submitted",
		"returned_for_revision" => "Returned for Revision",
		"resubmitted" => "Resubmitted",
		"approved" => "Approved",
		"rejected" => "Rejected",
		"stage_forwarded" => "Stage Forwarded",
		"remark_added" => "Remark Added",
		"status_updated" => "Status Updated",
		_ => "Action Recorded"
	};

	[JsonIgnore]
	public string DisplayActor => FirstNonEmpty(ActorName, ReviewerName, "—");

	[JsonIgnore]
	public string DisplayActorRole => FirstNonEmpty(ActorRole, StageName, Role, "—");

	[JsonIgnore]
	public string DisplayRemark => FirstNonEmpty(Remark, ReviewerComment, Comment, NewValue);

	[JsonIgnore]
	public string DisplayStatusAfterAction => FirstNonEmpty(CurrentStatusAfterAction, StatusAfterAction, Status);

	[JsonIgnore]
	public string DisplayProposalTitle => FirstNonEmpty(ProposalTitle, "Untitled proposal");

	[JsonIgnore]
	public string DisplayOrganizationName => FirstNonEmpty(OrganizationName, "Unknown organization");

	[JsonIgnore]
	public string DisplayAffectedFields => AffectedFields is { Length: > 0 }
		? string.Join(", ", AffectedFields.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()))
		: (string.IsNullOrWhiteSpace(FieldLabel) ? string.Empty : FieldLabel);

	[JsonIgnore]
	public string ActionIcon => EffectiveActionType switch
	{
		"submitted" => "⬆️",
		"returned_for_revision" => "↩️",
		"resubmitted" => "🔄",
		"approved" => "✅",
		"rejected" => "❌",
		"stage_forwarded" => "➡️",
		"remark_added" => "💬",
		"status_updated" => "ℹ️",
		_ => "🕘"
	};

	private static string FirstNonEmpty(params string?[] values)
	{
		for (var i = 0; i < values.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(values[i]))
			{
				return values[i]!.Trim();
			}
		}

		return string.Empty;
	}
}
