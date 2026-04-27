namespace docusystem.Models;

/// <summary>Which approval chain applies for this proposal.</summary>
public enum ApprovalFlowType
{
	/// <summary>Curricular events: Adviser → Program Chair → Dean → … → Executive Director.</summary>
	Academic,

	/// <summary>Non-curricular: Adviser first, then direct to SDAO chain (Program Chair and Dean skipped).</summary>
	NonAcademic
}
