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
			using var response = await client.GetAsync("api/proposals/pending", cancellationToken).ConfigureAwait(false);
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

	public async Task<Proposal?> GetProposalByIdAsync(int proposalId, CancellationToken cancellationToken = default)
	{
		if (IsSupabaseBackend())
		{
			return await GetByIdFromSupabaseAsync(proposalId, cancellationToken).ConfigureAwait(false);
		}

		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var response = await client.GetAsync($"api/proposals/{proposalId}", cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ParseSingleProposal(json);
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
			DecorateFlowType(list);

			return list;
		}

		if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
		{
			var list = data.Deserialize<List<Proposal>>(JsonOptions) ?? [];
			DecorateFlowType(list);

			return list;
		}

		return [];
	}

	private static Proposal? ParseSingleProposal(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;

		// Shape A: direct object proposal payload
		if (TryDeserializeProposal(root, out var direct))
		{
			direct!.ApprovalFlowType = ProposalWorkflowService.InferFlowTypeFromProposal(direct);
			return direct;
		}

		// Shape B: { data: {...} } or { proposal: {...} }
		if (root.ValueKind == JsonValueKind.Object)
		{
			if (root.TryGetProperty("data", out var data) && TryDeserializeProposal(data, out var fromData))
			{
				fromData!.ApprovalFlowType = ProposalWorkflowService.InferFlowTypeFromProposal(fromData);
				return fromData;
			}

			if (root.TryGetProperty("proposal", out var proposal) && TryDeserializeProposal(proposal, out var fromProposal))
			{
				fromProposal!.ApprovalFlowType = ProposalWorkflowService.InferFlowTypeFromProposal(fromProposal);
				return fromProposal;
			}

			// Shape C: { data: { proposal: {...} } }
			if (root.TryGetProperty("data", out var nestedData) &&
			    nestedData.ValueKind == JsonValueKind.Object &&
			    nestedData.TryGetProperty("proposal", out var nestedProposal) &&
			    TryDeserializeProposal(nestedProposal, out var nested))
			{
				nested!.ApprovalFlowType = ProposalWorkflowService.InferFlowTypeFromProposal(nested);
				return nested;
			}
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

	private static void DecorateFlowType(List<Proposal> list)
	{
		for (var i = 0; i < list.Count; i++)
		{
			list[i].ApprovalFlowType = ProposalWorkflowService.InferFlowTypeFromProposal(list[i]);
		}
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
}
