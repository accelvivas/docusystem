namespace docusystem.Models;

public class ApprovalStep
{
	public int StepNumber { get; set; }
	public string RoleName { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
}
