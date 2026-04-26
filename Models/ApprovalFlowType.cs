namespace docusystem.Models;

/// <summary>Which approval chain applies for this proposal.</summary>
public enum ApprovalFlowType
{
	/// <summary>Academic events: Adviser → … → Executive Director.</summary>
	Academic,

	/// <summary>Non-academic: starts at SDAO Assistant (no Adviser / Chair / Dean).</summary>
	NonAcademic
}
