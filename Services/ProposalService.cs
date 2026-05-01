using System.Net.Http.Json;
using System.Text.Json;
using docusystem.Models;
using docusystem.Models.Supabase;
using Supabase.Postgrest.Exceptions;

namespace docusystem.Services;

/// <summary>
/// Proposals — Laravel API or direct Supabase (<see cref="MobileDataOptions.Backend"/>).
/// </summary>
public sealed class ProposalService : IProposalService
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly AppSessionService _session;
	private readonly MobileDataOptions _dataOptions;
	private readonly SupabaseService _supabase;

	public ProposalService(
		IHttpClientFactory httpClientFactory,
		AppSessionService session,
		MobileDataOptions dataOptions,
		SupabaseService supabase)
	{
		_httpClientFactory = httpClientFactory;
		_session = session;
		_dataOptions = dataOptions;
		_supabase = supabase;
	}

	public async Task<IReadOnlyList<Proposal>> GetPendingApprovalsAsync(CancellationToken cancellationToken = default)
	{
		if (_session.CurrentUser is null)
		{
			return [];
		}

		if (IsSupabaseBackend())
		{
			return await GetPendingFromSupabaseAsync(cancellationToken).ConfigureAwait(false);
		}

		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var response = await client.GetAsync("api/approvals/pending", cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return [];
			}

			var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ParseProposalList(json);
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

	public async Task<IReadOnlyList<Proposal>> GetMySubmissionsAsync(CancellationToken cancellationToken = default)
	{
		if (_session.CurrentUser is null)
		{
			return [];
		}

		if (IsSupabaseBackend())
		{
			// Supabase mode currently uses a flat proposals table; fallback to the same list.
			return await GetPendingFromSupabaseAsync(cancellationToken).ConfigureAwait(false);
		}

		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");

			// Route fallbacks so mobile can work even when backend naming differs.
			var candidatePaths = new[]
			{
				"api/proposals/my-submissions",
				"api/my-submissions",
				"api/proposals/mine",
				"api/proposals?scope=my_submissions",
				"api/proposals?mine=1",
				"api/proposals?owned=1",
				// Broad fallback: many Laravel controllers scope this to the authenticated user.
				"api/proposals"
			};

			var merged = new List<Proposal>();
			var seenIds = new HashSet<int>();
			List<Proposal>? broadFallbackParsed = null;
			for (var i = 0; i < candidatePaths.Length; i++)
			{
				using var response = await client.GetAsync(candidatePaths[i], cancellationToken).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode)
				{
					// Only 401 means the session/token is invalid. For 403/404/405/422,
					// keep trying alternate route variants.
					if ((int)response.StatusCode is 401)
					{
						return [];
					}

					continue;
				}

				var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
				var parsed = ParseProposalList(json);
				if (parsed.Count == 0)
				{
					continue;
				}

				// Dedicated submitter routes are trusted as-is. The broad fallback
				// (`api/proposals`) often lists all proposals visible to the user, so
				// we filter client-side to keep only the current submitter's records
				// (including ones already returned for revision).
				var path = candidatePaths[i];
				var isBroadFallback = path.Equals("api/proposals", StringComparison.OrdinalIgnoreCase);
				if (isBroadFallback)
				{
					var owned = parsed
						.Where(p => IsOwnedByCurrentUser(p, _session.CurrentUser))
						.ToList();
					broadFallbackParsed = parsed.ToList();
					AddDistinct(merged, seenIds, owned);
					continue;
				}

				// Keep collecting from dedicated routes instead of returning early.
				// Some backends split by status and one endpoint may omit approved/archived records.
				AddDistinct(merged, seenIds, parsed);
			}

			if (merged.Count > 0)
			{
				return merged
					.OrderByDescending(p => p.SubmittedDate)
					.ToList();
			}

			// Last fallback for RSO tracking lane: many backends already scope `api/proposals`
			// to the authenticated user. If our ownership heuristics are too strict and
			// filtered everything out, return that scoped list so the user still sees
			// their submissions.
			if (IsRsoTrackingRole(_session.CurrentUser) && broadFallbackParsed is { Count: > 0 })
			{
				return broadFallbackParsed
					.OrderByDescending(p => p.SubmittedDate)
					.ToList();
			}

			return [];
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

	public async Task<Proposal?> GetProposalByIdAsync(int proposalId, CancellationToken cancellationToken = default)
	{
		if (IsSupabaseBackend())
		{
			return await GetByIdFromSupabaseAsync(proposalId, cancellationToken).ConfigureAwait(false);
		}

		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");

			// Try the canonical detail endpoint first, then a few common Laravel variants
			// in case the backend exposes proposal detail under a slightly different path.
			var candidatePaths = new[]
			{
				$"api/proposals/{proposalId}",
				$"api/proposals/{proposalId}?include=details",
				$"api/proposals/{proposalId}/details",
				$"api/proposals/{proposalId}/full",
				$"api/approvals/proposals/{proposalId}"
			};

			Proposal? best = null;
			for (var i = 0; i < candidatePaths.Length; i++)
			{
				using var response = await client.GetAsync(candidatePaths[i], cancellationToken).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode)
				{
					continue;
				}

				var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
				LogProposalKeys(candidatePaths[i], json);

				var parsed = ParseSingleProposal(json);
				if (parsed is null)
				{
					continue;
				}

				if (best is null)
				{
					best = parsed;
				}
				else
				{
					MergeProposalIntoBest(best, parsed);
					// Re-run normalize so any wrapper objects we just merged in get flattened
					// into top-level keys for downstream consumers.
					DecorateProposal(best);
				}

				// Keep probing all candidate endpoints and merge everything we can.
				// Some environments expose partial payloads on one path and full
				// detail payloads on another; early exit can leave many fields blank.
			}

			return best;
		}
		catch (HttpRequestException)
		{
			return null;
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public async Task<IReadOnlyList<ApprovalStep>> GetProposalWorkflowAsync(int proposalId, CancellationToken cancellationToken = default)
	{
		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var response = await client.GetAsync($"api/proposals/{proposalId}/workflow", cancellationToken)
				.ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return [];
			}

			var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ParseApprovalStepList(json);
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

	private static IReadOnlyList<ApprovalStep> ParseApprovalStepList(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return [];
		}

		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		if (root.ValueKind == JsonValueKind.Array)
		{
			return root.Deserialize<List<ApprovalStep>>(JsonOptions) ?? [];
		}

		if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
		{
			return data.Deserialize<List<ApprovalStep>>(JsonOptions) ?? [];
		}

		if (root.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
		{
			return steps.Deserialize<List<ApprovalStep>>(JsonOptions) ?? [];
		}

		return [];
	}

	public async Task<ApiActionResult> UpdateProposalAsync(Proposal proposal, CancellationToken cancellationToken = default)
	{
		if (IsSupabaseBackend())
		{
			return await UpdateInSupabaseAsync(proposal, cancellationToken).ConfigureAwait(false);
		}

		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var response = await client.PutAsJsonAsync($"api/proposals/{proposal.Id}", proposal, cancellationToken)
				.ConfigureAwait(false);
			if (response.IsSuccessStatusCode)
			{
				return ApiActionResult.Ok();
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ApiActionResult.Fail(ExtractMessage(body) ?? $"Update failed ({(int)response.StatusCode}).");
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

	public async Task<ApiActionResult> ResubmitProposalAsync(int proposalId, CancellationToken cancellationToken = default)
	{
		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");

			// Try the dedicated resubmit route first (POST). Falls back to PATCH/PUT shapes
			// the backend may use for status changes when /resubmit is not implemented.
			var attempts = new (HttpMethod Method, string Path, object? Body)[]
			{
				(HttpMethod.Post, $"api/proposals/{proposalId}/resubmit", new { }),
				(HttpMethod.Post, $"api/proposals/{proposalId}/resubmit", new { status = "pending" }),
				(HttpMethod.Patch, $"api/proposals/{proposalId}", new { status = "pending" }),
				(HttpMethod.Put, $"api/proposals/{proposalId}", new { status = "pending" })
			};

			HttpResponseMessage? lastResponse = null;
			for (var i = 0; i < attempts.Length; i++)
			{
				lastResponse?.Dispose();
				using var request = new HttpRequestMessage(attempts[i].Method, attempts[i].Path)
				{
					Content = attempts[i].Body is null
						? null
						: JsonContent.Create(attempts[i].Body, options: JsonOptions)
				};
				lastResponse = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

				if (lastResponse.IsSuccessStatusCode)
				{
					lastResponse.Dispose();
					return ApiActionResult.Ok("Proposal resubmitted.");
				}

				// 404/405 = route doesn't exist, try next variant. Stop on real failures.
				if ((int)lastResponse.StatusCode is not (404 or 405))
				{
					break;
				}
			}

			var body = lastResponse is null
				? string.Empty
				: await lastResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			var statusCode = lastResponse is null ? 0 : (int)lastResponse.StatusCode;
			lastResponse?.Dispose();

			return ApiActionResult.Fail(ExtractMessage(body) ?? $"Could not resubmit ({statusCode}).");
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

	private static bool IsOwnedByCurrentUser(Proposal proposal, User? user)
	{
		if (user is null)
		{
			return false;
		}

		var ext = proposal.ExtraData;
		if (user.Id > 0)
		{
			var submittedById =
				ReadIntFromExtra(ext, "submitted_by_id", "submitter_id", "created_by_id", "user_id") ??
				ReadNestedIntFromExtra(ext, "submitted_by", "id") ??
				ReadNestedIntFromExtra(ext, "submitter", "id") ??
				ReadNestedIntFromExtra(ext, "user", "id");

			if (submittedById.HasValue && submittedById.Value == user.Id)
			{
				return true;
			}
		}

		// RSO President scope: organization match is the strongest signal.
		if (!string.IsNullOrWhiteSpace(user.OrganizationName) &&
			!string.IsNullOrWhiteSpace(proposal.OrganizationName) &&
			string.Equals(proposal.OrganizationName.Trim(), user.OrganizationName.Trim(), StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (!string.IsNullOrWhiteSpace(user.OrganizationName) &&
			!string.IsNullOrWhiteSpace(proposal.OrganizationName))
		{
			var userOrg = user.OrganizationName.Trim();
			var proposalOrg = proposal.OrganizationName.Trim();
			if (proposalOrg.Contains(userOrg, StringComparison.OrdinalIgnoreCase) ||
			    userOrg.Contains(proposalOrg, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		// Fallback: API often stamps `submitted_by` with the user's display name or email.
		var submitted = proposal.SubmittedBy ?? string.Empty;
		if (string.IsNullOrWhiteSpace(submitted))
		{
			return false;
		}

		var candidates = new[]
		{
			user.DisplayName,
			user.Name,
			user.FullName,
			user.Email
		};

		for (var i = 0; i < candidates.Length; i++)
		{
			var c = candidates[i];
			if (!string.IsNullOrWhiteSpace(c) &&
				submitted.Contains(c, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static void AddDistinct(List<Proposal> target, HashSet<int> seenIds, IEnumerable<Proposal> source)
	{
		foreach (var p in source)
		{
			if (p is null)
			{
				continue;
			}

			if (p.Id > 0)
			{
				if (!seenIds.Add(p.Id))
				{
					continue;
				}

				target.Add(p);
				continue;
			}

			// No id from API: keep only one per title+org snapshot
			var exists = target.Any(x =>
				x.Id == 0 &&
				string.Equals(x.Title, p.Title, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(x.OrganizationName, p.OrganizationName, StringComparison.OrdinalIgnoreCase));
			if (!exists)
			{
				target.Add(p);
			}
		}
	}

	private static bool IsRsoTrackingRole(User? user)
	{
		if (user is null)
		{
			return false;
		}

		return string.Equals(user.Role, "RSO President", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(user.Role, "Organization Officer", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(user.RoleKey, "rso_president", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(user.RoleKey, "org_officer", StringComparison.OrdinalIgnoreCase);
	}

	private static int? ReadIntFromExtra(Dictionary<string, JsonElement>? ext, params string[] keys)
	{
		if (ext is null)
		{
			return null;
		}

		for (var i = 0; i < keys.Length; i++)
		{
			if (!ext.TryGetValue(keys[i], out var el))
			{
				continue;
			}

			if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
			{
				return n;
			}

			if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var parsed))
			{
				return parsed;
			}
		}

		return null;
	}

	private static int? ReadNestedIntFromExtra(Dictionary<string, JsonElement>? ext, string parent, string child)
	{
		if (ext is null || !ext.TryGetValue(parent, out var obj) || obj.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		if (!obj.TryGetProperty(child, out var value))
		{
			return null;
		}

		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
		{
			return n;
		}

		if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
		{
			return parsed;
		}

		return null;
	}

	private bool IsSupabaseBackend() =>
		string.Equals(_dataOptions.Backend, "Supabase", StringComparison.OrdinalIgnoreCase);

	private async Task<IReadOnlyList<Proposal>> GetPendingFromSupabaseAsync(CancellationToken cancellationToken)
	{
		if (!_supabase.IsAvailable || _supabase.Client is null)
		{
			return [];
		}

		try
		{
			// Optional: .Filter() here if you add a DB view or column for "pending for current user"
			var result = await _supabase.Client.From<ProposalRow>().Get().ConfigureAwait(false);
			var models = result.Models;
			if (models is null || models.Count == 0)
			{
				return [];
			}

			return models.Select(m => ProposalRowMapper.ToProposal(m, ApprovalFlowType.Academic)).ToList();
		}
		catch (PostgrestException)
		{
			return [];
		}
		catch (Exception)
		{
			return [];
		}
	}

	private async Task<Proposal?> GetByIdFromSupabaseAsync(int proposalId, CancellationToken cancellationToken)
	{
		if (!_supabase.IsAvailable || _supabase.Client is null)
		{
			return null;
		}

		try
		{
			var result = await _supabase.Client
				.From<ProposalRow>()
				.Where(x => x.Id == proposalId)
				.Get()
				.ConfigureAwait(false);

			var m = result.Models?.FirstOrDefault();
			return m is null ? null : ProposalRowMapper.ToProposal(m, ApprovalFlowType.Academic);
		}
		catch (PostgrestException)
		{
			return null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private async Task<ApiActionResult> UpdateInSupabaseAsync(Proposal proposal, CancellationToken cancellationToken)
	{
		if (!_supabase.IsAvailable || _supabase.Client is null)
		{
			return ApiActionResult.Fail("Supabase is not configured.");
		}

		try
		{
			var row = ProposalRowMapper.FromProposal(proposal);
			await _supabase.Client.From<ProposalRow>().Update(row, null, cancellationToken).ConfigureAwait(false);
			return ApiActionResult.Ok();
		}
		catch (PostgrestException ex)
		{
			return ApiActionResult.Fail(ex.Message);
		}
		catch (Exception ex)
		{
			return ApiActionResult.Fail(ex.Message);
		}
	}

	private static IReadOnlyList<Proposal> ParseProposalList(string json)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		if (root.ValueKind == JsonValueKind.Array)
		{
			var list = root.Deserialize<List<Proposal>>(JsonOptions) ?? [];
			DecorateProposals(list);

			return list;
		}

		if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
		{
			var list = data.Deserialize<List<Proposal>>(JsonOptions) ?? [];
			DecorateProposals(list);

			return list;
		}

		// Shape: { proposals: [...] }
		if (root.TryGetProperty("proposals", out var proposals) && proposals.ValueKind == JsonValueKind.Array)
		{
			var list = proposals.Deserialize<List<Proposal>>(JsonOptions) ?? [];
			DecorateProposals(list);

			return list;
		}

		// Shape: { data: { proposals: [...] } }
		if (root.TryGetProperty("data", out var nestedData) &&
		    nestedData.ValueKind == JsonValueKind.Object &&
		    nestedData.TryGetProperty("proposals", out var nestedProposals) &&
		    nestedProposals.ValueKind == JsonValueKind.Array)
		{
			var list = nestedProposals.Deserialize<List<Proposal>>(JsonOptions) ?? [];
			DecorateProposals(list);

			return list;
		}

		// Shape: { data: { items: [...] } } (common paginator wrappers)
		if (root.TryGetProperty("data", out var wrappedData) &&
		    wrappedData.ValueKind == JsonValueKind.Object &&
		    wrappedData.TryGetProperty("items", out var items) &&
		    items.ValueKind == JsonValueKind.Array)
		{
			var list = items.Deserialize<List<Proposal>>(JsonOptions) ?? [];
			DecorateProposals(list);

			return list;
		}

		// Shape: { results: [...] } / { rows: [...] } / { payload: [...] }
		foreach (var key in new[] { "results", "rows", "payload" })
		{
			if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
			{
				var list = arr.Deserialize<List<Proposal>>(JsonOptions) ?? [];
				DecorateProposals(list);

				return list;
			}
		}

		// Fallback: recursively search common wrappers until we find an array payload.
		if (TryFindProposalArray(root, out var discovered))
		{
			var list = discovered.Deserialize<List<Proposal>>(JsonOptions) ?? [];
			DecorateProposals(list);
			return list;
		}

		return [];
	}

	private static bool TryFindProposalArray(JsonElement node, out JsonElement found)
	{
		if (node.ValueKind == JsonValueKind.Array)
		{
			found = node;
			return true;
		}

		if (node.ValueKind != JsonValueKind.Object)
		{
			found = default;
			return false;
		}

		ReadOnlySpan<string> preferredKeys = ["data", "items", "rows", "results", "payload", "proposals", "list"];
		for (var i = 0; i < preferredKeys.Length; i++)
		{
			if (!node.TryGetProperty(preferredKeys[i], out var child))
			{
				continue;
			}

			if (child.ValueKind == JsonValueKind.Array)
			{
				found = child;
				return true;
			}

			if (child.ValueKind == JsonValueKind.Object && TryFindProposalArray(child, out found))
			{
				return true;
			}
		}

		foreach (var prop in node.EnumerateObject())
		{
			if (prop.Value.ValueKind == JsonValueKind.Object && TryFindProposalArray(prop.Value, out found))
			{
				return true;
			}
		}

		found = default;
		return false;
	}

	private static Proposal? ParseSingleProposal(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;

		// IMPORTANT:
		// Try wrapper shapes first. Because Proposal has JsonExtensionData, directly
		// deserializing the outer object (e.g. { data: {...} }) can "succeed" while
		// actually producing an almost-empty proposal.
		// Shape A: { data: {...} } / { data: { proposal: {...} } } / { proposal: {...} }
		if (root.ValueKind == JsonValueKind.Object)
		{
			if (root.TryGetProperty("data", out var data) &&
			    data.ValueKind == JsonValueKind.Object &&
			    data.TryGetProperty("proposal", out var nestedProposal) &&
			    TryDeserializeProposal(nestedProposal, out var nested))
			{
				DecorateProposal(nested!);
				return nested;
			}

			if (root.TryGetProperty("data", out var dataNode) && TryDeserializeProposal(dataNode, out var fromData))
			{
				DecorateProposal(fromData!);
				return fromData;
			}

			if (root.TryGetProperty("proposal", out var proposal) && TryDeserializeProposal(proposal, out var fromProposal))
			{
				DecorateProposal(fromProposal!);
				return fromProposal;
			}

		}

		// Shape B: direct object proposal payload
		if (TryDeserializeProposal(root, out var direct))
		{
			DecorateProposal(direct!);
			return direct;
		}

		return null;
	}

	private static bool TryDeserializeProposal(JsonElement element, out Proposal? proposal)
	{
		proposal = null;
		if (element.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		try
		{
			proposal = element.Deserialize<Proposal>(JsonOptions);
			return proposal is not null;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static void DecorateProposals(List<Proposal> list)
	{
		for (var i = 0; i < list.Count; i++)
		{
			DecorateProposal(list[i]);
		}
	}

	/// <summary>Projects nested API fields into canonical model props and infers flow type.</summary>
	private static void DecorateProposal(Proposal proposal)
	{
		Proposal.NormalizeFromExtensionData(proposal);
		proposal.ApprovalFlowType = ProposalWorkflowService.InferFlowTypeFromProposal(proposal);
	}

	private static string? ExtractMessage(string json)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
			{
				return m.GetString();
			}
		}
		catch
		{
		}

		return null;
	}

	/// <summary>
	/// Keys that signal a "detailed" proposal payload (not just queue summary). When the API
	/// returns at least one of these, we stop probing alternate detail endpoints.
	/// </summary>
	private static readonly string[] DetailKeys =
	[
		"proposed_start_date", "proposed_end_date", "proposed_start_time", "proposed_end_time",
		"target_sdg", "source_of_funding", "school_code", "program", "activity_types",
		"overall_goal", "specific_objectives", "criteria_mechanics", "program_flow",
		"academic_term", "estimated_budget", "venue", "activity_description", "budget_items_payload"
	];

	private static bool HasDetailFields(Proposal? p)
	{
		if (p is null) return false;
		if (!string.IsNullOrWhiteSpace(p.Description)) return true;
		if (!string.IsNullOrWhiteSpace(p.Venue)) return true;
		if (p.ActivityDate != default) return true;
		if (p.Budget > 0m) return true;

		var ext = p.ExtraData;
		if (ext is null) return false;
		for (var i = 0; i < DetailKeys.Length; i++)
		{
			if (ext.ContainsKey(DetailKeys[i]))
			{
				return true;
			}
		}
		return false;
	}

	private static void MergeProposalIntoBest(Proposal best, Proposal extra)
	{
		// Fill canonical fields first.
		if (string.IsNullOrWhiteSpace(best.Title) && !string.IsNullOrWhiteSpace(extra.Title)) best.Title = extra.Title;
		if (string.IsNullOrWhiteSpace(best.OrganizationName) && !string.IsNullOrWhiteSpace(extra.OrganizationName)) best.OrganizationName = extra.OrganizationName;
		if (string.IsNullOrWhiteSpace(best.SubmittedBy) && !string.IsNullOrWhiteSpace(extra.SubmittedBy)) best.SubmittedBy = extra.SubmittedBy;
		if (string.IsNullOrWhiteSpace(best.CurrentStage) && !string.IsNullOrWhiteSpace(extra.CurrentStage)) best.CurrentStage = extra.CurrentStage;
		if (string.IsNullOrWhiteSpace(best.Description) && !string.IsNullOrWhiteSpace(extra.Description)) best.Description = extra.Description;
		if (string.IsNullOrWhiteSpace(best.Venue) && !string.IsNullOrWhiteSpace(extra.Venue)) best.Venue = extra.Venue;
		if (string.IsNullOrWhiteSpace(best.Status) && !string.IsNullOrWhiteSpace(extra.Status)) best.Status = extra.Status;
		if (best.ActivityDate == default && extra.ActivityDate != default) best.ActivityDate = extra.ActivityDate;
		if (best.SubmittedDate == default && extra.SubmittedDate != default) best.SubmittedDate = extra.SubmittedDate;
		if (best.Budget == 0m && extra.Budget > 0m) best.Budget = extra.Budget;

		// Merge extension data — keys present in `extra` but missing in `best` get added.
		if (extra.ExtraData is not null)
		{
			best.ExtraData ??= new();
			foreach (var kv in extra.ExtraData)
			{
				if (!best.ExtraData.ContainsKey(kv.Key))
				{
					best.ExtraData[kv.Key] = kv.Value;
				}
			}
		}
	}

	[System.Diagnostics.Conditional("DEBUG")]
	private static void LogProposalKeys(string path, string json)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			JsonElement target = root;
			if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data))
			{
				target = data.ValueKind == JsonValueKind.Object ? data : root;
			}
			if (target.ValueKind != JsonValueKind.Object)
			{
				return;
			}

			// Top-level keys.
			var keys = new List<string>();
			foreach (var prop in target.EnumerateObject())
			{
				keys.Add(prop.Name);
			}
			System.Diagnostics.Debug.WriteLine($"[PROPOSAL KEYS @ {path}] {string.Join(", ", keys)}");

			// Nested wrapper keys — many backend payloads nest the real fields inside one of
			// these, so log their child keys to make mismatches obvious in the debug output.
			string[] wrappers =
			[
				"proposal", "proposal_data", "details",
				"activity_request_form", "proposal_form", "request_form",
				"data", "attributes", "form", "form_data"
			];
			for (var i = 0; i < wrappers.Length; i++)
			{
				if (!target.TryGetProperty(wrappers[i], out var w) || w.ValueKind != JsonValueKind.Object)
				{
					continue;
				}

				var sub = new List<string>();
				foreach (var prop in w.EnumerateObject())
				{
					sub.Add(prop.Name);
				}
				if (sub.Count > 0)
				{
					System.Diagnostics.Debug.WriteLine($"[PROPOSAL NESTED \"{wrappers[i]}\" KEYS @ {path}] {string.Join(", ", sub)}");
				}
			}
		}
		catch
		{
		}
	}

}
