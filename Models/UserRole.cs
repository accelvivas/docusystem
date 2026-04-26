using System.Text.Json.Serialization;

namespace docusystem.Models;

/// <summary>Row from <c>roles</c> (FK on user) — <c>name</c>, <c>display_name</c>, <c>approval_level</c>.</summary>
public sealed class UserRole
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("display_name")]
	public string? DisplayName { get; set; }

	/// <summary>Workflow order (1–7 in seed data); <c>NULL</c> e.g. for <c>rso_president</c>.</summary>
	[JsonPropertyName("approval_level")]
	public int? ApprovalLevel { get; set; }
}
