using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Ordered signatories for Academic vs Non-Academic events (client-side until the API supplies a full chain).
/// </summary>
public static class ProposalWorkflowService
{
	private static readonly IReadOnlyList<string> Academic =
	[
		"Adviser",
		"Program Chair",
		"Dean",
		"SDAO Assistant",
		"SDAO Coordinator",
		"Academic Services",
		"Academic Director",
		"Executive Director"
	];

	private static readonly IReadOnlyList<string> NonAcademic =
	[
		"SDAO Assistant",
		"SDAO Coordinator",
		"Academic Services",
		"Academic Director",
		"Executive Director"
	];

	/// <summary>Every role name that can appear as a signatory in either flow.</summary>
	public static readonly IReadOnlyList<string> AllSignatoryRoles = Academic
		.Concat(NonAcademic)
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
		.ToList();

	public static IReadOnlyList<string> GetStages(ApprovalFlowType flowType) =>
		flowType == ApprovalFlowType.NonAcademic ? NonAcademic : Academic;

	public static int IndexOfStage(string stageName, ApprovalFlowType flowType)
	{
		var stages = GetStages(flowType);
		for (var i = 0; i < stages.Count; i++)
		{
			if (string.Equals(stages[i], stageName, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}

		return -1;
	}

	public static bool RoleAppearsInFlow(string roleName, ApprovalFlowType flowType) =>
		GetStages(flowType).Any(s => string.Equals(s, roleName, StringComparison.OrdinalIgnoreCase));

	public static bool IsAnySignatoryRole(string roleName) =>
		AllSignatoryRoles.Any(s => string.Equals(s, roleName, StringComparison.OrdinalIgnoreCase));

	public static string GetEventTypeDisplay(ApprovalFlowType flowType) =>
		flowType == ApprovalFlowType.NonAcademic ? "Non-Academic" : "Academic";

	public static string GetFlowChainSummary(ApprovalFlowType flowType) =>
		flowType == ApprovalFlowType.NonAcademic
			? "SDAO Assistant → SDAO Coordinator → Academic Services → Academic Director → Executive Director"
			: "Adviser → Program Chair → Dean → SDAO Assistant → SDAO Coordinator → Academic Services → Academic Director → Executive Director";

	public static string GetFlowHelperText(ApprovalFlowType flowType) =>
		flowType == ApprovalFlowType.NonAcademic
			? "Non-academic: routing starts at SDAO (Adviser, Chair, and Dean are skipped)."
			: "Academic: routing from Adviser through Executive Director.";

	public static string GetSkippedStagesNote(ApprovalFlowType flowType) =>
		flowType == ApprovalFlowType.NonAcademic
			? "Skipped: Adviser, Program Chair, Dean."
			: string.Empty;

	/// <summary>Single short line for proposal details UI (full order is on the approval steps list).</summary>
	public static string GetCompactWorkflowNote(ApprovalFlowType flowType)
	{
		var helper = GetFlowHelperText(flowType);
		var skipped = GetSkippedStagesNote(flowType);
		return string.IsNullOrEmpty(skipped) ? helper : $"{helper} {skipped}";
	}
}
