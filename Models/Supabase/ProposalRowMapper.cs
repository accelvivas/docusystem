using docusystem.Models;

namespace docusystem.Models.Supabase;

public static class ProposalRowMapper
{
	public static Proposal ToProposal(ProposalRow r, ApprovalFlowType flowType = ApprovalFlowType.Academic)
	{
		return new Proposal
		{
			Id = r.Id,
			Title = r.Title ?? string.Empty,
			OrganizationName = r.OrganizationName ?? string.Empty,
			SubmittedBy = r.SubmittedBy ?? string.Empty,
			CurrentStage = r.CurrentStage ?? string.Empty,
			Status = r.Status ?? string.Empty,
			ActivityDate = r.ActivityDate ?? default,
			Venue = r.Venue ?? string.Empty,
			Budget = r.Budget ?? 0m,
			Description = r.Description ?? string.Empty,
			CanEdit = r.CanEdit ?? false,
			CanApprove = r.CanApprove ?? false,
			SubmittedDate = r.SubmittedDate ?? default,
			FullyApprovedAt = r.FullyApprovedAt,
			LastRemarks = r.LastRemarks,
			ApprovalFlowType = flowType
		};
	}

	public static void ApplyProposal(Proposal p, ProposalRow r)
	{
		r.Id = p.Id;
		r.Title = p.Title;
		r.OrganizationName = p.OrganizationName;
		r.SubmittedBy = p.SubmittedBy;
		r.CurrentStage = p.CurrentStage;
		r.Status = p.Status;
		r.ActivityDate = p.ActivityDate;
		r.Venue = p.Venue;
		r.Budget = p.Budget;
		r.Description = p.Description;
		r.CanEdit = p.CanEdit;
		r.CanApprove = p.CanApprove;
		r.SubmittedDate = p.SubmittedDate;
		r.FullyApprovedAt = p.FullyApprovedAt;
		r.LastRemarks = p.LastRemarks;
	}

	public static ProposalRow FromProposal(Proposal p)
	{
		var r = new ProposalRow();
		ApplyProposal(p, r);
		return r;
	}
}
