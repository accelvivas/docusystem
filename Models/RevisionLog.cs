using System.Text.Json.Serialization;

namespace docusystem.Models;

/// <summary>
/// Single row in the unified revision timeline returned by
/// <c>ProposalRevisionController@index</c>. The controller merges three sources
/// (field-level reviews, approval logs, and workflow steps in <c>revision_required</c>),
/// so <see cref="Type"/> tells the UI which kind of row this is and
/// <see cref="Comment"/> carries the human-readable note.
/// </summary>
public class RevisionLog
{
	/// <summary>
	/// Composite ID like <c>field_5</c>, <c>log_12</c>, or <c>step_7</c> — kept as
	/// <see cref="string"/> so we can deserialize it as-is.
	/// </summary>
	[JsonPropertyName("id")]
	public string Id { get; set; } = string.Empty;

	/// <summary><c>field_revision</c>, <c>workflow_revision</c>, or <c>workflow_step_revision</c>.</summary>
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

	// ───────────────────────────────────────────────────────────────────────
	// Backwards-compatible read helpers (UI bindings keep working)
	// ───────────────────────────────────────────────────────────────────────

	/// <summary>UI label for "what was changed" — falls back to step/role name.</summary>
	[JsonIgnore]
	public string FieldChanged => string.IsNullOrWhiteSpace(FieldLabel) ? "Record" : FieldLabel;

	/// <summary>Display name of who left the note; defaults to "—" when the API doesn't include it.</summary>
	[JsonIgnore]
	public string EditedBy => string.IsNullOrWhiteSpace(ReviewerName) ? "—" : ReviewerName!;

	/// <summary>Friendly label for the row category (used in the legacy "role" UI slot).</summary>
	[JsonIgnore]
	public string Role => Type switch
	{
		"field_revision" => "Field-level revision",
		"workflow_revision" => "Workflow revision",
		"workflow_step_revision" => "Workflow step revision",
		_ => string.IsNullOrWhiteSpace(Type) ? "Revision" : Type
	};

	/// <summary>API doesn't return a "before" snapshot in the new contract.</summary>
	[JsonIgnore]
	public string OldValue => string.Empty;

	/// <summary>The reviewer's note — primary text the UI should render.</summary>
	[JsonIgnore]
	public string NewValue => Comment ?? string.Empty;
}
