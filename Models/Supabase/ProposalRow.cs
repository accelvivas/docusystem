using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace docusystem.Models.Supabase;

/// <summary>Maps to Postgres table (default <c>proposals</c>). Rename <see cref="TableAttribute"/> if your schema differs.</summary>
[Table("proposals")]
public class ProposalRow : BaseModel
{
	[PrimaryKey("id")]
	[Column("id")]
	public int Id { get; set; }

	[Column("title")]
	public string Title { get; set; } = string.Empty;

	[Column("organization_name")]
	public string OrganizationName { get; set; } = string.Empty;

	[Column("submitted_by")]
	public string SubmittedBy { get; set; } = string.Empty;

	[Column("current_stage")]
	public string CurrentStage { get; set; } = string.Empty;

	[Column("status")]
	public string Status { get; set; } = string.Empty;

	[Column("activity_date")]
	public DateTime? ActivityDate { get; set; }

	[Column("venue")]
	public string Venue { get; set; } = string.Empty;

	[Column("budget")]
	public decimal? Budget { get; set; }

	[Column("description")]
	public string Description { get; set; } = string.Empty;

	[Column("can_edit")]
	public bool? CanEdit { get; set; }

	[Column("can_approve")]
	public bool? CanApprove { get; set; }

	[Column("submitted_date")]
	public DateTime? SubmittedDate { get; set; }

	[Column("fully_approved_at")]
	public DateTime? FullyApprovedAt { get; set; }

	[Column("last_remarks")]
	public string? LastRemarks { get; set; }
}
