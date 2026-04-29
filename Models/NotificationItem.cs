using System.Text.Json.Serialization;

namespace docusystem.Models;

/// <summary>
/// Aligns 1:1 with <c>NotificationController@index</c> (Laravel):
/// <c>{ id, title, message, type, link_url, read_at, created_at }</c>.
/// <see cref="IsRead"/> is derived from <see cref="ReadAt"/> so callers can keep
/// using the same boolean check the UI already relies on.
/// </summary>
public class NotificationItem
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	/// <summary>API maps <c>notifications.body</c> to <c>message</c>.</summary>
	[JsonPropertyName("message")]
	public string Message { get; set; } = string.Empty;

	[JsonPropertyName("type")]
	public string Type { get; set; } = string.Empty;

	/// <summary>Optional deep link the UI can open when the notification is tapped.</summary>
	[JsonPropertyName("link_url")]
	public string? LinkUrl { get; set; }

	/// <summary>Server timestamp when the notification was marked as read; <c>null</c> means unread.</summary>
	[JsonPropertyName("read_at")]
	public DateTime? ReadAt { get; set; }

	[JsonPropertyName("created_at")]
	public DateTime DateCreated { get; set; }

	/// <summary>True when <see cref="ReadAt"/> has a value.</summary>
	[JsonIgnore]
	public bool IsRead => ReadAt.HasValue;

	/// <summary>Reserved for future contract additions; not sent by the current controller.</summary>
	[JsonPropertyName("proposal_id")]
	public int ProposalId { get; set; }

	/// <summary>Reserved for future contract additions; not sent by the current controller.</summary>
	[JsonPropertyName("recipient_role")]
	public string RecipientRole { get; set; } = string.Empty;
}
