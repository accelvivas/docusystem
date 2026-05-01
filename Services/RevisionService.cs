using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using docusystem.Models;

namespace docusystem.Services;

/// <summary>Revision history from the Laravel API.</summary>
public sealed class RevisionService : IRevisionService
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IProposalService _proposalService;

	public RevisionService(
		IHttpClientFactory httpClientFactory,
		IProposalService proposalService)
	{
		_httpClientFactory = httpClientFactory;
		_proposalService = proposalService;
	}

	public async Task<IReadOnlyList<RevisionLog>> GetRevisionHistoryAsync(int proposalId, CancellationToken cancellationToken = default)
	{
		var visible = await _proposalService.GetProposalByIdAsync(proposalId, cancellationToken).ConfigureAwait(false);
		if (visible is null)
		{
			return [];
		}

		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			var candidatePaths = new[]
			{
				$"api/proposals/{proposalId}/history",
				$"api/proposals/{proposalId}/revision-history",
				$"api/proposals/{proposalId}/revisions"
			};

			for (var i = 0; i < candidatePaths.Length; i++)
			{
				using var response = await client.GetAsync(candidatePaths[i], cancellationToken).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode)
				{
					if ((int)response.StatusCode == 401)
					{
						return [];
					}

					continue;
				}

				var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
				var parsed = ParseRevisionList(json, visible);
				if (parsed.Count > 0)
				{
					return parsed;
				}
			}

			return BuildHistoryFromProposalPayload(visible);
		}
		catch (HttpRequestException)
		{
			return BuildHistoryFromProposalPayload(visible);
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return BuildHistoryFromProposalPayload(visible);
		}
		catch (JsonException)
		{
			return BuildHistoryFromProposalPayload(visible);
		}
	}

	public async Task<IReadOnlyList<FieldReviewEntry>> GetFieldReviewsAsync(int proposalId, CancellationToken cancellationToken = default)
	{
		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var response = await client.GetAsync($"api/proposals/{proposalId}/field-reviews", cancellationToken)
				.ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return [];
			}

			var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ParseFieldReviewList(json);
		}
		catch (HttpRequestException)
		{
			return [];
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return [];
		}
		catch (JsonException)
		{
			return [];
		}
	}

	public async Task<ApiActionResult> SubmitFieldChangesAsync(
		int proposalId,
		IReadOnlyList<FieldChange> changes,
		CancellationToken cancellationToken = default)
	{
		if (changes is null || changes.Count == 0)
		{
			return ApiActionResult.Fail("There are no field reviews to submit.");
		}

		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			var payload = new
			{
				field_reviews = changes
			};

			using var response = await client.PostAsJsonAsync(
				$"api/proposals/{proposalId}/field-reviews",
				payload,
				cancellationToken).ConfigureAwait(false);

			if (response.IsSuccessStatusCode)
			{
				return ApiActionResult.Ok("Field reviews saved.");
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ApiActionResult.Fail(ExtractMessage(body) ?? $"Could not save field reviews ({(int)response.StatusCode}).");
		}
		catch (HttpRequestException)
		{
			return ApiActionResult.Fail("Cannot reach the server.");
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return ApiActionResult.Fail("Request timed out.");
		}
	}

	private static string? ExtractMessage(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			using var doc = JsonDocument.Parse(json);
			if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
			{
				return m.GetString();
			}
		}
		catch (JsonException)
		{
		}

		return null;
	}

	private static IReadOnlyList<FieldReviewEntry> ParseFieldReviewList(string json)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		if (root.ValueKind == JsonValueKind.Array)
		{
			return root.Deserialize<List<FieldReviewEntry>>(JsonOptions) ?? [];
		}

		if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
		{
			return data.Deserialize<List<FieldReviewEntry>>(JsonOptions) ?? [];
		}

		return [];
	}

	private static IReadOnlyList<RevisionLog> ParseRevisionList(string json, Proposal proposal)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;

		if (!TryGetHistoryArray(root, out var arr))
		{
			return [];
		}

		var list = arr.Deserialize<List<RevisionLog>>(JsonOptions) ?? [];
		if (list.Count == 0)
		{
			return [];
		}

		var meta = ReadHistoryMeta(root);
		for (var i = 0; i < list.Count; i++)
		{
			NormalizeHistoryRow(
				list[i],
				meta.ProposalId != 0 ? meta.ProposalId : proposal.Id,
				FirstNonEmpty(meta.ProposalTitle, proposal.Title),
				FirstNonEmpty(meta.OrganizationName, proposal.OrganizationName));
		}

		return list
			.Where(r => r.Timestamp != default)
			.OrderByDescending(r => r.Timestamp)
			.ToList();
	}

	private static bool TryGetHistoryArray(JsonElement root, out JsonElement arr)
	{
		if (root.ValueKind == JsonValueKind.Array)
		{
			arr = root;
			return true;
		}

		if (root.ValueKind == JsonValueKind.Object)
		{
			ReadOnlySpan<string> keys = ["entries", "history", "revisions", "logs", "data"];
			for (var i = 0; i < keys.Length; i++)
			{
				if (!root.TryGetProperty(keys[i], out var value))
				{
					continue;
				}

				if (value.ValueKind == JsonValueKind.Array)
				{
					arr = value;
					return true;
				}

				if (value.ValueKind == JsonValueKind.Object)
				{
					ReadOnlySpan<string> nestedKeys = ["entries", "history", "revisions", "logs", "data"];
					for (var j = 0; j < nestedKeys.Length; j++)
					{
						if (value.TryGetProperty(nestedKeys[j], out var nested) && nested.ValueKind == JsonValueKind.Array)
						{
							arr = nested;
							return true;
						}
					}
				}
			}
		}

		arr = default;
		return false;
	}

	private static (int ProposalId, string? ProposalTitle, string? OrganizationName) ReadHistoryMeta(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Object)
		{
			return default;
		}

		var scope = root;
		if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
		{
			scope = data;
		}

		var proposalId = ReadInt(scope, "proposal_id", "id") ?? 0;
		var proposalTitle = ReadString(scope, "proposal_title", "activity_title", "title");
		var orgName = ReadString(scope, "organization_name", "organization");
		return (proposalId, proposalTitle, orgName);
	}

	private static void NormalizeHistoryRow(RevisionLog row, int proposalId, string proposalTitle, string organizationName)
	{
		row.ProposalId = row.ProposalId != 0 ? row.ProposalId : proposalId;
		row.ProposalTitle = FirstNonEmpty(row.ProposalTitle, proposalTitle);
		row.OrganizationName = FirstNonEmpty(row.OrganizationName, organizationName);

		if (row.Timestamp == default && row.ActedAt.HasValue)
		{
			row.Timestamp = row.ActedAt.Value;
		}

		if (string.IsNullOrWhiteSpace(row.ActionType))
		{
			row.ActionType = DeriveActionType(row.Status, row.Type, row.Comment);
		}

		if (string.IsNullOrWhiteSpace(row.StatusAfterAction) &&
		    string.IsNullOrWhiteSpace(row.CurrentStatusAfterAction))
		{
			row.StatusAfterAction = row.Status;
		}

		if (string.IsNullOrWhiteSpace(row.ActorName))
		{
			row.ActorName = row.ReviewerName;
		}

		if (string.IsNullOrWhiteSpace(row.Remark))
		{
			row.Remark = FirstNonEmpty(row.ReviewerComment, row.Comment);
		}
	}

	private static string DeriveActionType(string? statusRaw, string? typeRaw, string? comment)
	{
		var status = (statusRaw ?? string.Empty).Trim().ToLowerInvariant();
		var type = (typeRaw ?? string.Empty).Trim().ToLowerInvariant();

		if (status == "submitted")
		{
			return "submitted";
		}
		if (status is "rejected")
		{
			return "rejected";
		}
		if (status is "revision_required" or "returned_for_revision")
		{
			return "returned_for_revision";
		}
		if (status is "resubmitted")
		{
			return "resubmitted";
		}
		if (status is "approved" or "completed")
		{
			return "approved";
		}
		if (status is "pending_next_approval" or "forwarded")
		{
			return "stage_forwarded";
		}
		if (type is "field_revision")
		{
			return "remark_added";
		}
		if (!string.IsNullOrWhiteSpace(comment))
		{
			return "remark_added";
		}

		return "status_updated";
	}

	private static IReadOnlyList<RevisionLog> BuildHistoryFromProposalPayload(Proposal proposal)
	{
		var logs = new List<RevisionLog>();
		var ext = proposal.ExtraData;
		var baseTitle = FirstNonEmpty(proposal.Title, "Untitled proposal");
		var baseOrg = FirstNonEmpty(proposal.OrganizationName, "Unknown organization");

		if (proposal.SubmittedDate != default)
		{
			logs.Add(new RevisionLog
			{
				Id = $"submitted_{proposal.Id}",
				ProposalId = proposal.Id,
				ProposalTitle = baseTitle,
				OrganizationName = baseOrg,
				ActionType = "submitted",
				ActorName = proposal.SubmittedBy,
				ActorRole = "Submitter",
				StageName = "Submitted",
				Timestamp = proposal.SubmittedDate,
				StatusAfterAction = "Submitted"
			});
		}

		if (ext is not null && TryReadArray(ext, "workflow_summary", out var workflowRows))
		{
			var stepOrder = 1;
			foreach (var row in workflowRows.EnumerateArray())
			{
				if (row.ValueKind != JsonValueKind.Object)
				{
					continue;
				}

				var status = ReadString(row, "status") ?? string.Empty;
				var roleName = ReadString(row, "role_name") ?? $"Stage {stepOrder}";
				var actorName = ReadString(row, "assigned_to");
				var comments = ReadString(row, "review_comments");
				var actedAt = ReadDate(row, "acted_at");
				var actionType = DeriveActionType(status, "workflow_step_revision", comments);

				// Skip untouched pending step entries without action evidence.
				if (!actedAt.HasValue &&
				    string.IsNullOrWhiteSpace(comments) &&
				    status.Equals("pending", StringComparison.OrdinalIgnoreCase))
				{
					stepOrder++;
					continue;
				}

				logs.Add(new RevisionLog
				{
					Id = $"workflow_{proposal.Id}_{stepOrder}_{actionType}",
					ProposalId = proposal.Id,
					ProposalTitle = baseTitle,
					OrganizationName = baseOrg,
					ActionType = actionType,
					ActorName = actorName,
					ActorRole = roleName,
					StageName = roleName,
					Remark = comments,
					StatusAfterAction = status,
					Timestamp = actedAt ?? proposal.SubmittedDate
				});

				if (!string.IsNullOrWhiteSpace(comments) && actionType != "returned_for_revision")
				{
					logs.Add(new RevisionLog
					{
						Id = $"workflow_remark_{proposal.Id}_{stepOrder}",
						ProposalId = proposal.Id,
						ProposalTitle = baseTitle,
						OrganizationName = baseOrg,
						ActionType = "remark_added",
						ActorName = actorName,
						ActorRole = roleName,
						StageName = roleName,
						Remark = comments,
						StatusAfterAction = status,
						Timestamp = actedAt ?? proposal.SubmittedDate
					});
				}

				stepOrder++;
			}
		}

		if (ext is not null && TryReadArray(ext, "revision_comments", out var revisionRows))
		{
			var idx = 1;
			foreach (var row in revisionRows.EnumerateArray())
			{
				if (row.ValueKind != JsonValueKind.Object)
				{
					continue;
				}

				logs.Add(new RevisionLog
				{
					Id = $"revision_comment_{proposal.Id}_{idx++}",
					ProposalId = proposal.Id,
					ProposalTitle = baseTitle,
					OrganizationName = baseOrg,
					ActionType = "returned_for_revision",
					ActorName = ReadString(row, "reviewer_name"),
					ActorRole = FirstNonEmpty(ReadString(row, "role_name"), ReadString(row, "stage_name")),
					StageName = ReadString(row, "role_name"),
					Remark = ReadString(row, "comment"),
					StatusAfterAction = "Returned for Revision",
					Timestamp = ReadDate(row, "acted_at", "created_at") ?? proposal.SubmittedDate
				});
			}
		}

		if (ext is not null && TryReadArray(ext, "field_reviews", out var fieldReviewRows))
		{
			var idx = 1;
			foreach (var row in fieldReviewRows.EnumerateArray())
			{
				if (row.ValueKind != JsonValueKind.Object)
				{
					continue;
				}

				var status = ReadString(row, "status") ?? string.Empty;
				var note = ReadString(row, "comment");
				var fieldLabel = ReadString(row, "field_label");
				if (string.IsNullOrWhiteSpace(note) && !status.Equals("revision", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				logs.Add(new RevisionLog
				{
					Id = $"field_review_{proposal.Id}_{idx++}",
					ProposalId = proposal.Id,
					ProposalTitle = baseTitle,
					OrganizationName = baseOrg,
					ActionType = status.Equals("revision", StringComparison.OrdinalIgnoreCase) ? "returned_for_revision" : "remark_added",
					ActorName = ReadString(row, "reviewer_name"),
					ActorRole = ReadString(row, "stage_name"),
					StageName = ReadString(row, "stage_name"),
					Remark = note,
					AffectedFields = string.IsNullOrWhiteSpace(fieldLabel) ? null : [fieldLabel],
					StatusAfterAction = status,
					Timestamp = ReadDate(row, "reviewed_at", "created_at") ?? proposal.SubmittedDate
				});
			}
		}

		return logs
			.Where(l => l.Timestamp != default)
			.OrderByDescending(l => l.Timestamp)
			.ToList();
	}

	private static bool TryReadArray(Dictionary<string, JsonElement> ext, string key, out JsonElement value)
	{
		if (ext.TryGetValue(key, out value) && value.ValueKind == JsonValueKind.Array)
		{
			return true;
		}

		value = default;
		return false;
	}

	private static string? ReadString(JsonElement obj, params string[] keys)
	{
		for (var i = 0; i < keys.Length; i++)
		{
			if (!obj.TryGetProperty(keys[i], out var value))
			{
				continue;
			}

			if (value.ValueKind == JsonValueKind.String)
			{
				var s = value.GetString();
				if (!string.IsNullOrWhiteSpace(s))
				{
					return s.Trim();
				}
			}
			else if (value.ValueKind == JsonValueKind.Number)
			{
				return value.ToString();
			}
		}

		return null;
	}

	private static int? ReadInt(JsonElement obj, params string[] keys)
	{
		for (var i = 0; i < keys.Length; i++)
		{
			if (!obj.TryGetProperty(keys[i], out var value))
			{
				continue;
			}

			if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
			{
				return n;
			}

			if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
			{
				return parsed;
			}
		}

		return null;
	}

	private static DateTime? ReadDate(JsonElement obj, params string[] keys)
	{
		for (var i = 0; i < keys.Length; i++)
		{
			if (!obj.TryGetProperty(keys[i], out var value) || value.ValueKind != JsonValueKind.String)
			{
				continue;
			}

			if (DateTime.TryParse(value.GetString(), out var dt))
			{
				return dt;
			}
		}

		return null;
	}

	private static string FirstNonEmpty(params string?[] values)
	{
		for (var i = 0; i < values.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(values[i]))
			{
				return values[i]!.Trim();
			}
		}

		return string.Empty;
	}
}
