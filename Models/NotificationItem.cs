using System.Text.Json.Serialization;

namespace docusystem.Models;

public class NotificationItem
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("proposal_id")]
	public int ProposalId { get; set; }

	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[JsonPropertyName("message")]
	public string Message { get; set; } = string.Empty;

	[JsonPropertyName("created_at")]
	public DateTime DateCreated { get; set; }

	[JsonPropertyName("is_read")]
	public bool IsRead { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; } = string.Empty;

	[JsonPropertyName("recipient_role")]
	public string RecipientRole { get; set; } = string.Empty;
}
