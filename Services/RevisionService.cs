using System.Net.Http.Json;
using System.Text.Json;
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
			using var response = await client.GetAsync($"api/proposals/{proposalId}/revisions", cancellationToken)
				.ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return [];
			}

			var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ParseRevisionList(json);
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

	private static IReadOnlyList<RevisionLog> ParseRevisionList(string json)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		if (root.ValueKind == JsonValueKind.Array)
		{
			return root.Deserialize<List<RevisionLog>>(JsonOptions) ?? [];
		}

		if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
		{
			return data.Deserialize<List<RevisionLog>>(JsonOptions) ?? [];
		}

		return [];
	}
}
