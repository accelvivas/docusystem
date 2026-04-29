using System.Text.Json.Serialization;
using System.Text.Json;

namespace docusystem.Models;

/// <summary>
/// Proposal summary/detail. Canonical property names match the existing UI; the
/// <see cref="NormalizeFromExtensionData"/> helper projects nested fields from the
/// new Laravel API contract (e.g. <c>organization.name</c>, <c>current_step.role_name</c>,
/// <c>activity_title</c>, <c>submission_date</c>) into these canonical fields after
/// deserialization. Unknown fields are captured by <see cref="ExtraData"/>.
/// </summary>
public class Proposal
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[JsonPropertyName("organization_name")]
	public string OrganizationName { get; set; } = string.Empty;

	[JsonPropertyName("submitted_by")]
	public string SubmittedBy { get; set; } = string.Empty;

	[JsonPropertyName("current_stage")]
	public string CurrentStage { get; set; } = string.Empty;

	[JsonPropertyName("status")]
	public string Status { get; set; } = string.Empty;

	[JsonPropertyName("activity_date")]
	public DateTime ActivityDate { get; set; }

	[JsonPropertyName("venue")]
	public string Venue { get; set; } = string.Empty;

	[JsonPropertyName("budget")]
	public decimal Budget { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; } = string.Empty;

	/// <summary>Server-computed: whether the current user may edit this proposal.</summary>
	[JsonPropertyName("can_edit")]
	public bool CanEdit { get; set; }

	/// <summary>Server-computed: whether the current user may approve at this stage.</summary>
	[JsonPropertyName("can_approve")]
	public bool CanApprove { get; set; }

	[JsonPropertyName("submitted_date")]
	public DateTime SubmittedDate { get; set; }

	/// <summary>Optional — set when status is fully approved (Laravel timestamp).</summary>
	[JsonPropertyName("fully_approved_at")]
	public DateTime? FullyApprovedAt { get; set; }

	/// <summary>Latest return-for-revision remarks from the reviewer (combined from <c>revision_comments</c>).</summary>
	[JsonIgnore]
	public string? LastRemarks { get; set; }

	/// <summary>
	/// Whether this proposal follows the Academic or Non-Academic signatory chain (map from API when available).
	/// </summary>
	[JsonIgnore]
	public ApprovalFlowType ApprovalFlowType { get; set; } = ApprovalFlowType.Academic;

	/// <summary>
	/// Captures extra JSON fields from Laravel (e.g. <c>activity_title</c>, nested <c>organization</c>,
	/// <c>current_step</c>, <c>workflow_summary</c>, etc.) so the UI can consume new backend keys
	/// without waiting for model schema updates. Populated by <see cref="JsonExtensionDataAttribute"/>.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtraData { get; set; }

	/// <summary>
	/// Maps the API contract returned by ProposalController/ApprovalController into this canonical
	/// model. Safe to call multiple times — each canonical field is filled only when empty.
	/// </summary>
	public static void NormalizeFromExtensionData(Proposal? p)
	{
		if (p is null)
		{
			return;
		}

		var ext = p.ExtraData;

		if (ext is not null)
		{
			if (string.IsNullOrWhiteSpace(p.Title))
			{
				p.Title = ReadString(ext, "proposal_title", "activity_title") ?? p.Title;
			}

			if (string.IsNullOrWhiteSpace(p.Description))
			{
				p.Description = ReadString(ext, "activity_description") ?? p.Description;
			}

			if (string.IsNullOrWhiteSpace(p.OrganizationName))
			{
				p.OrganizationName =
					ReadNestedString(ext, "organization", "name", "organization_name") ??
					ReadString(ext, "organization_name") ??
					p.OrganizationName;
			}

			if (string.IsNullOrWhiteSpace(p.SubmittedBy))
			{
				p.SubmittedBy =
					ReadNestedString(ext, "submitted_by", "name") ??
					ReadString(ext, "submitted_by_name") ??
					p.SubmittedBy;
			}

			if (string.IsNullOrWhiteSpace(p.CurrentStage))
			{
				p.CurrentStage =
					ReadNestedString(ext, "current_step", "role_name") ??
					ReadString(ext, "current_stage") ??
					p.CurrentStage;
			}

			if (p.SubmittedDate == default)
			{
				p.SubmittedDate =
					ReadDateTime(ext, "submission_date", "submitted_at", "submitted_date", "pending_since") ??
					p.SubmittedDate;
			}

			if (p.ActivityDate == default)
			{
				p.ActivityDate =
					ReadDateTime(ext, "proposed_start_date", "activity_date") ??
					p.ActivityDate;
			}

			if (p.Budget == 0m)
			{
				p.Budget = ReadDecimal(ext, "estimated_budget") ?? p.Budget;
			}

			if (string.IsNullOrWhiteSpace(p.Venue))
			{
				p.Venue = ReadString(ext, "venue") ?? p.Venue;
			}

			if (string.IsNullOrWhiteSpace(p.LastRemarks))
			{
				p.LastRemarks = ReadRevisionComments(ext);
			}
		}

		p.Status = NormalizeStatus(p.Status);
	}

	/// <summary>
	/// Maps lowercase Laravel status enums (<c>pending</c>, <c>under_review</c>,
	/// <c>revision_required</c>, <c>approved</c>, etc.) to the human-friendly
	/// strings the UI expects (<c>Pending</c>, <c>Under Review</c>,
	/// <c>Returned for Revision</c>, <c>Approved</c>, <c>Fully Approved</c>, <c>Rejected</c>).
	/// </summary>
	public static string NormalizeStatus(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return string.Empty;
		}

		var key = raw.Trim();
		return key.ToLowerInvariant() switch
		{
			"pending" => "Pending",
			"submitted" => "Submitted",
			"under_review" => "Under Review",
			"pending_next_approval" => "Under Review",
			"revision_required" => "Returned for Revision",
			"revision" => "Returned for Revision",
			"returned_for_revision" => "Returned for Revision",
			"approved" => "Approved",
			"final_approved" => "Fully Approved",
			"fully_approved" => "Fully Approved",
			"rejected" => "Rejected",
			_ => key
		};
	}

	private static string? ReadString(Dictionary<string, JsonElement> ext, params string[] keys)
	{
		for (var i = 0; i < keys.Length; i++)
		{
			if (!ext.TryGetValue(keys[i], out var el))
			{
				continue;
			}

			if (el.ValueKind == JsonValueKind.String)
			{
				var s = el.GetString();
				if (!string.IsNullOrWhiteSpace(s))
				{
					return s.Trim();
				}
			}
			else if (el.ValueKind == JsonValueKind.Number)
			{
				return el.ToString();
			}
		}

		return null;
	}

	private static string? ReadNestedString(Dictionary<string, JsonElement> ext, string parentKey, params string[] childKeys)
	{
		if (!ext.TryGetValue(parentKey, out var parent) || parent.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		for (var i = 0; i < childKeys.Length; i++)
		{
			if (!parent.TryGetProperty(childKeys[i], out var v))
			{
				continue;
			}

			if (v.ValueKind == JsonValueKind.String)
			{
				var s = v.GetString();
				if (!string.IsNullOrWhiteSpace(s))
				{
					return s.Trim();
				}
			}
			else if (v.ValueKind == JsonValueKind.Number)
			{
				return v.ToString();
			}
		}

		return null;
	}

	private static DateTime? ReadDateTime(Dictionary<string, JsonElement> ext, params string[] keys)
	{
		for (var i = 0; i < keys.Length; i++)
		{
			if (!ext.TryGetValue(keys[i], out var el) || el.ValueKind != JsonValueKind.String)
			{
				continue;
			}

			var s = el.GetString();
			if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
				System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
				out var dt))
			{
				return dt;
			}
		}

		return null;
	}

	private static decimal? ReadDecimal(Dictionary<string, JsonElement> ext, params string[] keys)
	{
		for (var i = 0; i < keys.Length; i++)
		{
			if (!ext.TryGetValue(keys[i], out var el))
			{
				continue;
			}

			if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
			{
				return d;
			}

			if (el.ValueKind == JsonValueKind.String &&
				decimal.TryParse(el.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ds))
			{
				return ds;
			}
		}

		return null;
	}

	private static string? ReadRevisionComments(Dictionary<string, JsonElement> ext)
	{
		if (!ext.TryGetValue("revision_comments", out var arr) || arr.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		var lines = new List<string>();
		foreach (var item in arr.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			if (!item.TryGetProperty("comment", out var commentEl) || commentEl.ValueKind != JsonValueKind.String)
			{
				continue;
			}

			var comment = commentEl.GetString();
			if (string.IsNullOrWhiteSpace(comment))
			{
				continue;
			}

			string? role = null;
			if (item.TryGetProperty("role_name", out var roleEl) && roleEl.ValueKind == JsonValueKind.String)
			{
				role = roleEl.GetString();
			}

			lines.Add(string.IsNullOrWhiteSpace(role) ? comment! : $"{role}: {comment}");
		}

		return lines.Count == 0 ? null : string.Join('\n', lines);
	}
}
