namespace docusystem.Models;

public class ApprovalStep
{
	public int StepNumber { get; set; }
	public string RoleName { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;

	/// <summary>Name of the user who acted on this step (if available).</summary>
	public string? ReviewedBy { get; set; }

	/// <summary>When this step was acted on (approved, returned, etc.).</summary>
	public DateTime? ActedAt { get; set; }
}
