using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Legacy entry point for the <strong>Academic</strong> chain only.
/// Prefer <see cref="ProposalWorkflowService.GetStages"/> with a proposal's <see cref="Proposal.ApprovalFlowType"/>.
/// </summary>
public static class ApprovalWorkflow
{
	public static IReadOnlyList<string> Stages =>
		ProposalWorkflowService.GetStages(ApprovalFlowType.Academic);

	public static int IndexOfStage(string stage) =>
		ProposalWorkflowService.IndexOfStage(stage, ApprovalFlowType.Academic);
}
