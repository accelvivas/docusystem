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
					Status = status,
					IsCurrentStep = index == currentIndex && currentIndex >= 0,
				};
			})
			.ToList();
	}

	public async Task<ApiActionResult> ApproveProposalAsync(int proposalId, CancellationToken cancellationToken = default)
	{
		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			// Controller validates `comments` as nullable; sending empty string keeps the
			// shape consistent with the web flow.
			using var response = await client.PostAsJsonAsync(
				$"api/proposals/{proposalId}/approve",
				new
				{
					comments = string.Empty,
					action_source = "mobile",
					action_timestamp = DateTime.UtcNow
				},
				JsonOptions,
				cancellationToken).ConfigureAwait(false);

			if (response.IsSuccessStatusCode)
			{
				return ApiActionResult.Ok("Proposal approved.");
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ApiActionResult.Fail(ResolveActionFailureMessage(response.StatusCode, body, "Approval failed"));
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
		var comments = (remarks ?? string.Empty).Trim();
		if (string.IsNullOrEmpty(comments))
		{
			return ApiActionResult.Fail("Please add revision notes before returning the proposal.");
		}

		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var response = await client.PostAsJsonAsync(
				$"api/proposals/{proposalId}/return",
				new
				{
					comments,
					action_source = "mobile",
					action_timestamp = DateTime.UtcNow
				},
				JsonOptions,
				cancellationToken).ConfigureAwait(false);

			if (response.IsSuccessStatusCode)
			{
				return ApiActionResult.Ok("The proposal was returned. The RSO President can edit and address your notes.");
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ApiActionResult.Fail(ResolveActionFailureMessage(response.StatusCode, body, "Could not return proposal"));
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

	public async Task<ApiActionResult> RejectProposalAsync(int proposalId, string? reason, CancellationToken cancellationToken = default)
	{
		var comments = (reason ?? string.Empty).Trim();
		if (string.IsNullOrEmpty(comments))
		{
			return ApiActionResult.Fail("Please provide a reason before rejecting.");
		}

		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var response = await client.PostAsJsonAsync(
				$"api/proposals/{proposalId}/reject",
				new
				{
					comments,
					action_source = "mobile",
					action_timestamp = DateTime.UtcNow
				},
				JsonOptions,
				cancellationToken).ConfigureAwait(false);

			if (response.IsSuccessStatusCode)
			{
				return ApiActionResult.Ok("The proposal has been rejected.");
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ApiActionResult.Fail(ResolveActionFailureMessage(response.StatusCode, body, "Could not reject proposal"));
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

	/// <summary>
	/// Builds a clearer mobile message for failed approve/return/reject calls. When the
	/// backend returns a stage-assignment 403 we surface specific guidance so users see
	/// the real reason instead of a generic “Approval failed” line.
	/// </summary>
	private static string ResolveActionFailureMessage(System.Net.HttpStatusCode status, string body, string defaultPrefix)
	{
		var serverMessage = ExtractMessage(body);
		if ((int)status == 403 &&
		    !string.IsNullOrWhiteSpace(serverMessage) &&
		    serverMessage.Contains("assigned approver", StringComparison.OrdinalIgnoreCase))
		{
			return serverMessage +
			       " (The proposal’s current step is not linked to your account. Please ask the web admin to confirm the workflow step assignment for this proposal.)";
		}

		return serverMessage ?? $"{defaultPrefix} ({(int)status}).";
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
