using System.Text.Json.Serialization;

namespace docusystem.Models;

public class RevisionLog
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("proposal_id")]
	public int ProposalId { get; set; }

	[JsonPropertyName("edited_by")]
	public string EditedBy { get; set; } = string.Empty;

	[JsonPropertyName("role")]
	public string Role { get; set; } = string.Empty;

	[JsonPropertyName("field_changed")]
	public string FieldChanged { get; set; } = string.Empty;

	[JsonPropertyName("old_value")]
	public string OldValue { get; set; } = string.Empty;

	[JsonPropertyName("new_value")]
	public string NewValue { get; set; } = string.Empty;

	[JsonPropertyName("created_at")]
	public DateTime Timestamp { get; set; }
}
