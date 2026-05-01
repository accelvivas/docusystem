using System.Text.Json.Serialization;

namespace docusystem.Models;

/// <summary>
/// Notification DTO for approver-focused mobile updates.
/// Supports both old and new Laravel payload shapes.
/// </summary>
public class NotificationItem
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("proposal_id")]
	public int ProposalId { get; set; }

	[JsonPropertyName("proposal_title")]
	public string? ProposalTitle { get; set; }

	[JsonPropertyName("activity_title")]
	public string? ActivityTitle { get; set; }

	[JsonPropertyName("organization_name")]
	public string? OrganizationName { get; set; }

	[JsonPropertyName("organization")]
	public string? Organization { get; set; }

	[JsonPropertyName("notification_type")]
	public string? NotificationType { get; set; }

	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[JsonPropertyName("message_title")]
	public string? MessageTitle { get; set; }

	[JsonPropertyName("message")]
	public string Message { get; set; } = string.Empty;

	[JsonPropertyName("message_body")]
	public string? MessageBody { get; set; }

	[JsonPropertyName("body")]
	public string? Body { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; } = string.Empty;

	[JsonPropertyName("current_status")]
	public string? CurrentStatus { get; set; }

	[JsonPropertyName("status")]
	public string? Status { get; set; }

	[JsonPropertyName("stage_name")]
	public string? StageName { get; set; }

	[JsonPropertyName("stage")]
	public string? Stage { get; set; }

	[JsonPropertyName("reviewer_level")]
	public string? ReviewerLevel { get; set; }

	[JsonPropertyName("actor_name")]
	public string? ActorName { get; set; }

	[JsonPropertyName("short_remark")]
	public string? ShortRemark { get; set; }

	[JsonPropertyName("screen_target")]
	public string? ScreenTarget { get; set; }

	[JsonPropertyName("link_url")]
	public string? LinkUrl { get; set; }

	[JsonPropertyName("linkUrl")]
	public string? LinkUrlCamel { get; set; }

	[JsonPropertyName("read_at")]
	public DateTime? ReadAt { get; set; }

	[JsonPropertyName("is_read")]
	public bool? IsReadFlag { get; set; }

	[JsonPropertyName("created_at")]
	public DateTime DateCreated { get; set; }

	[JsonIgnore]
	public string? ResolvedLinkUrl => LinkUrl ?? LinkUrlCamel;

	[JsonIgnore]
	public string TypeKey =>
		FirstNonEmpty(NotificationType, Type).ToLowerInvariant();

	[JsonIgnore]
	public bool IsRead => ReadAt.HasValue || IsReadFlag == true;

	[JsonIgnore]
	public string DisplayTitle =>
		FirstNonEmpty(MessageTitle, Title, BuildFallbackTitle());

	[JsonIgnore]
	public string DisplayMessage =>
		FirstNonEmpty(MessageBody, Message, Body, BuildFallbackBody());

	[JsonIgnore]
	public string DisplayProposalTitle => FirstNonEmpty(ProposalTitle, ActivityTitle, "Untitled proposal");

	[JsonIgnore]
	public string DisplayOrganizationName => FirstNonEmpty(OrganizationName, Organization, "Unknown organization");

	[JsonIgnore]
	public string DisplayStage => FirstNonEmpty(StageName, ReviewerLevel, Stage);

	[JsonIgnore]
	public string DisplayStatus => FirstNonEmpty(CurrentStatus, Status);

	[JsonIgnore]
	public string IconGlyph => TypeKey switch
	{
		"new_pending_proposal" => "📋",
		"approval_reminder" => "🔔",
		"proposal_returned_for_revision" => "↩️",
		"proposal_resubmitted" => "🔄",
		"proposal_approved" => "✅",
		"proposal_rejected" => "❌",
		"status_updated" => "ℹ️",
		"revision_history_updated" => "🕘",
		_ => "🔔"
	};

	private string BuildFallbackTitle()
	{
		return TypeKey switch
		{
			"new_pending_proposal" => "New Pending Proposal",
			"approval_reminder" => "Approval Reminder",
			"proposal_returned_for_revision" => "Proposal Returned for Revision",
			"proposal_resubmitted" => "Proposal Resubmitted",
			"proposal_approved" => "Proposal Approved",
			"proposal_rejected" => "Proposal Rejected",
			"status_updated" => "Status Updated",
			"revision_history_updated" => "Revision History Updated",
			_ => "Notification"
		};
	}

	private string BuildFallbackBody()
	{
		return TypeKey switch
		{
			"new_pending_proposal" => "Status: Waiting for your review",
			"approval_reminder" => "You have pending proposal(s) for review.",
			"proposal_returned_for_revision" => FirstNonEmpty(ShortRemark, "The proposal was returned for revision."),
			"proposal_resubmitted" => "The revised proposal is ready for your review.",
			"proposal_approved" => "Your approval has been recorded.",
			"proposal_rejected" => "This proposal was rejected.",
			"status_updated" => FirstNonEmpty(CurrentStatus, "Proposal status has changed."),
			"revision_history_updated" => "A new reviewer remark was added.",
			_ => "Approval workflow update."
		};
	}

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
