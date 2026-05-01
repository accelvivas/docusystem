using System.Text.Json;
using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Ordered signatories for Curricular vs Non-curricular events
/// (client-side until the API supplies a full chain).
/// </summary>
public static class ProposalWorkflowService
{
	// Curricular: Adviser -> Program Chair -> Dean -> SDAO chain -> Executive Director
	private static readonly IReadOnlyList<string> Curricular =
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

	// Non-curricular: starts at Adviser, then jumps directly to SDAO chain.
	private static readonly IReadOnlyList<string> NonCurricular =
	[
		"Adviser",
		"SDAO Assistant",
		"SDAO Coordinator",
		"Academic Services",
		"Academic Director",
		"Executive Director"
	];

	/// <summary>Every role name that can appear as a signatory in either flow.</summary>
	public static readonly IReadOnlyList<string> AllSignatoryRoles = Curricular
		.Concat(NonCurricular)
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
		.ToList();

	public static IReadOnlyList<string> GetStages(ApprovalFlowType flowType) =>
		flowType == ApprovalFlowType.NonAcademic ? NonCurricular : Curricular;

	public static int IndexOfStage(string stageName, ApprovalFlowType flowType)
	{
		var stages = GetStages(flowType);
		for (var i = 0; i < stages.Count; i++)
		{
			if (StageNamesEquivalent(stages[i], stageName))
			{
				return i;
			}
		}

		return -1;
	}

	public static bool RoleAppearsInFlow(string roleName, ApprovalFlowType flowType) =>
		GetStages(flowType).Any(s => StageNamesEquivalent(s, roleName));

	public static bool IsAnySignatoryRole(string roleName) =>
		AllSignatoryRoles.Any(s => StageNamesEquivalent(s, roleName));

	/// <summary>
	/// Public alias-aware role comparison for permission checks.
	/// </summary>
	public static bool IsEquivalentRole(string? left, string? right) =>
		StageNamesEquivalent(left, right);

	public static string GetEventTypeDisplay(ApprovalFlowType flowType) =>
		flowType == ApprovalFlowType.NonAcademic ? "Non-curricular" : "Curricular";

	public static string GetFlowChainSummary(ApprovalFlowType flowType) =>
		flowType == ApprovalFlowType.NonAcademic
			? "Adviser → SDAO Assistant → SDAO Coordinator → Academic Services → Academic Director → Executive Director"
			: "Adviser → Program Chair → Dean → SDAO Assistant → SDAO Coordinator → Academic Services → Academic Director → Executive Director";

	public static string GetFlowHelperText(ApprovalFlowType flowType) =>
		flowType == ApprovalFlowType.NonAcademic
			? "Non-curricular: routing starts at Adviser, then proceeds directly to SDAO signatories."
			: "Curricular: routing starts at Adviser then continues through signatories.";

	public static string GetSkippedStagesNote(ApprovalFlowType flowType) =>
		flowType == ApprovalFlowType.NonAcademic
			? "Skipped: Program Chair, Dean."
			: string.Empty;

	/// <summary>
	/// Infer proposal flow from backend wording (e.g. "Curricular" / "Non-curricular").
	/// Defaults to Curricular when missing/unknown.
	/// </summary>
	public static ApprovalFlowType InferFlowTypeFromText(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return ApprovalFlowType.Academic;
		}

		var v = text.Trim().ToLowerInvariant();
		if (v.Contains("non-curricular") || v.Contains("non curricular") || v.Contains("noncurricular"))
		{
			return ApprovalFlowType.NonAcademic;
		}

		if (v.Contains("curricular"))
		{
			return ApprovalFlowType.Academic;
		}

		// Keep historical aliases too.
		if (v.Contains("non-academic") || v.Contains("non academic"))
		{
			return ApprovalFlowType.NonAcademic;
		}

		return ApprovalFlowType.Academic;
	}

	/// <summary>Infer flow from known proposal extra-data keys.</summary>
	public static ApprovalFlowType InferFlowTypeFromProposal(Proposal proposal)
	{
		var ext = proposal.ExtraData;
		var fromKeys = FirstNonEmpty(
			GetExtraString(ext, "nature_of_activity"),
			GetExtraString(ext, "activity_nature"),
			GetExtraString(ext, "event_type"),
			GetExtraString(ext, "flow_type"),
			GetExtraString(ext, "proposal_type"));
		return InferFlowTypeFromText(fromKeys);
	}

	private static string? GetExtraString(Dictionary<string, JsonElement>? ext, string key)
	{
		if (ext is null || !ext.TryGetValue(key, out var el))
		{
			return null;
		}

		return el.ValueKind switch
		{
			JsonValueKind.String => el.GetString(),
			JsonValueKind.Number => el.ToString(),
			_ => null
		};
	}

	/// <summary>
	/// Accept backend/staff aliases for the same SDAO stages.
	/// This keeps flow logic working when APIs return "SDAO Staff"
	/// while the configured stage labels are "SDAO Assistant"/"SDAO Coordinator".
	/// </summary>
	private static bool StageNamesEquivalent(string? left, string? right)
	{
		if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
		{
			return false;
		}

		var l = NormalizeStageKey(left);
		var r = NormalizeStageKey(right);
		return string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeStageKey(string raw)
	{
		var key = raw.Trim().ToLowerInvariant();
		key = key.Replace("_", " ", StringComparison.Ordinal);
		key = key.Replace("-", " ", StringComparison.Ordinal);
		while (key.Contains("  ", StringComparison.Ordinal))
		{
			key = key.Replace("  ", " ", StringComparison.Ordinal);
		}

		// Shared SDAO alias bucket
		if (key is "sdao staff" or "sdao assistant" or "sdao coordinator")
		{
			return "sdao staff";
		}

		return key;
	}

	private static string? FirstNonEmpty(params string?[] values)
	{
		for (var i = 0; i < values.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(values[i]))
			{
				return values[i]!.Trim();
			}
		}
		return null;
	}

	/// <summary>Single short line for proposal details UI (full order is on the approval steps list).</summary>
	public static string GetCompactWorkflowNote(ApprovalFlowType flowType)
	{
		var helper = GetFlowHelperText(flowType);
		var skipped = GetSkippedStagesNote(flowType);
		return string.IsNullOrEmpty(skipped) ? helper : $"{helper} {skipped}";
	}
}
