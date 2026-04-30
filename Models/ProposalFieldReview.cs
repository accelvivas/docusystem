namespace docusystem.Models;

public enum FieldReviewState { Pending, Passed, Revision }

/// <summary>Single field inside a proposal step that an approver marks as Passed or Needs Revision.</summary>
public class ProposalFieldReview
{
	public string Label { get; set; } = string.Empty;
	public string Value { get; set; } = string.Empty;
	public string StepKey { get; set; } = "step1";
	public bool IsFile { get; set; }
	public ProposalAttachment? Attachment { get; set; }
	public List<BudgetTableRow> BudgetRows { get; set; } = [];
	public FieldReviewState State { get; set; } = FieldReviewState.Pending;
	public string RevisionNote { get; set; } = string.Empty;

	/// <summary>Runtime-only; tracks whether the revision note input is expanded.</summary>
	public bool RevisionInputVisible { get; set; }
}

public class BudgetTableRow
{
	public string Material { get; set; } = string.Empty;
	public decimal Quantity { get; set; }
	public decimal UnitPrice { get; set; }
	public decimal Price => Quantity * UnitPrice;
}
