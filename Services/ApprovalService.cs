using System.Net.Http.Json;
using System.Text.Json;
using docusystem.Models;

namespace docusystem.Services;

/// <summary>Approval workflow UI and sign-off actions via the Laravel API.</summary>
public sealed class ApprovalService : IApprovalService
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly IHttpClientFactory _httpClientFactory;

	public ApprovalService(IHttpClientFactory httpClientFactory)
	{
		_httpClientFactory = httpClientFactory;
	}

	/// <summary>Academic chain only — prefer building steps from <see cref="BuildApprovalSteps"/> per proposal.</summary>
	public static readonly IReadOnlyList<string> ApprovalSequence = ProposalWorkflowService.GetStages(ApprovalFlowType.Academic);

	public IReadOnlyList<ApprovalStep> BuildApprovalSteps(Proposal proposal)
	{
		var stages = ProposalWorkflowService.GetStages(proposal.ApprovalFlowType);
		var currentIndex = ProposalWorkflowService.IndexOfStage(proposal.CurrentStage, proposal.ApprovalFlowType);

		return stages
			.Select((role, index) =>
			{
				var status = "Locked";

				if (string.Equals(proposal.Status, "Fully Approved", StringComparison.OrdinalIgnoreCase))
				{
					status = "Completed";
				}
				else if (index < currentIndex)
				{
					status = "Completed";
				}
				else if (index == currentIndex && currentIndex >= 0)
				{
					status = string.Equals(proposal.Status, "Returned for Revision", StringComparison.OrdinalIgnoreCase)
						? "Current (Returned)"
						: "Current";
				}
				else if (string.Equals(proposal.Status, "Returned for Revision", StringComparison.OrdinalIgnoreCase))
				{
					status = "Locked";
				}
				else
				{
					status = "Pending";
				}

				return new ApprovalStep
				{
					StepNumber = index + 1,
					RoleName = role,
					Status = status
				};
			})
			.ToList();
	}

	public async Task<ApiActionResult> ApproveProposalAsync(int proposalId, CancellationToken cancellationToken = default)
	{
		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var response = await client.PostAsync($"api/proposals/{proposalId}/approve", null, cancellationToken)
				.ConfigureAwait(false);

			if (response.IsSuccessStatusCode)
			{
				return ApiActionResult.Ok();
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ApiActionResult.Fail(ExtractMessage(body) ?? $"Approval failed ({(int)response.StatusCode}).");
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

	public async Task<ApiActionResult> ReturnProposalAsync(int proposalId, string? remarks, CancellationToken cancellationToken = default)
	{
		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var response = await client.PostAsJsonAsync(
				$"api/proposals/{proposalId}/return",
				new { remarks },
				JsonOptions,
				cancellationToken).ConfigureAwait(false);

			if (response.IsSuccessStatusCode)
			{
				return ApiActionResult.Ok("The proposal was sent back. The RSO President can edit and address your remarks.");
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ApiActionResult.Fail(ExtractMessage(body) ?? $"Could not return proposal ({(int)response.StatusCode}).");
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
}
