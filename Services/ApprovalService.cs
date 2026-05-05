using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using docusystem.Models;

namespace docusystem.Services;

/// <summary>Approval workflow UI and sign-off actions via the Laravel API.</summary>
public sealed class ApprovalService : IApprovalService
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	/// <summary>Snake_case body keys must match Laravel — do not camelCase dictionary keys.</summary>
	private static readonly JsonSerializerOptions WritePayloadOptions = new()
	{
		PropertyNamingPolicy = null,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IProposalService _proposalService;

	public ApprovalService(IHttpClientFactory httpClientFactory, IProposalService proposalService)
	{
		_httpClientFactory = httpClientFactory;
		_proposalService = proposalService;
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

			// Many backends bind approve to a specific workflow row; web sends step id implicitly.
			// Without it, POST can succeed while no row advances — web stays on the same signatory.
			var workflowSteps = await _proposalService.GetProposalWorkflowAsync(proposalId, cancellationToken).ConfigureAwait(false);
			var currentStep = ResolveCurrentWorkflowStep(workflowSteps);

			var payload = new Dictionary<string, object?>
			{
				["comments"] = string.Empty,
				["action_source"] = "mobile",
				["action_timestamp"] = DateTime.UtcNow
			};
			if (currentStep?.Id > 0)
			{
				var sid = currentStep.Id;
				payload["workflow_step_id"] = sid;
				payload["step_id"] = sid;
				payload["approval_workflow_step_id"] = sid;
			}

			using var content = new StringContent(
				JsonSerializer.Serialize(payload, WritePayloadOptions),
				Encoding.UTF8,
				"application/json");

			using var response = await client.PostAsync($"api/proposals/{proposalId}/approve", content, cancellationToken).ConfigureAwait(false);

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

	/// <summary>Picks the server-marked current step, else the earliest step that looks actionable.</summary>
	private static ApprovalStep? ResolveCurrentWorkflowStep(IReadOnlyList<ApprovalStep> steps)
	{
		if (steps is null || steps.Count == 0)
		{
			return null;
		}

		var flagged = steps.Where(static s => s.IsCurrentStep).OrderBy(static s => s.StepNumber).ToList();
		if (flagged.Count > 0)
		{
			return flagged[0];
		}

		foreach (var s in steps.OrderBy(static x => x.StepNumber))
		{
			if (WorkflowStepStatusLooksActive(s.Status))
			{
				return s;
			}
		}

		return null;
	}

	private static bool WorkflowStepStatusLooksActive(string? status)
	{
		if (string.IsNullOrWhiteSpace(status))
		{
			return false;
		}

		var key = status.Trim().ToLowerInvariant()
			.Replace(" ", "_", StringComparison.Ordinal);

		return key is "pending" or "current" or "in_progress" or "active" or "waiting" or "pending_review";
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

			var workflowSteps = await _proposalService.GetProposalWorkflowAsync(proposalId, cancellationToken).ConfigureAwait(false);
			var currentStep = ResolveCurrentWorkflowStep(workflowSteps);

			var payload = new Dictionary<string, object?>
			{
				["comments"] = comments,
				["action_source"] = "mobile",
				["action_timestamp"] = DateTime.UtcNow
			};
			if (currentStep?.Id > 0)
			{
				var sid = currentStep.Id;
				payload["workflow_step_id"] = sid;
				payload["step_id"] = sid;
				payload["approval_workflow_step_id"] = sid;
			}

			using var content = new StringContent(
				JsonSerializer.Serialize(payload, WritePayloadOptions),
				Encoding.UTF8,
				"application/json");

			using var response = await client.PostAsync($"api/proposals/{proposalId}/return", content, cancellationToken).ConfigureAwait(false);

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
