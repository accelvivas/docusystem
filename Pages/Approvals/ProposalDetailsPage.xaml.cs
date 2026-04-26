namespace docusystem.Pages.Approvals;

using docusystem.Models;
using docusystem.Services;

/// <summary>
/// Proposal details and approval actions — calls <see cref="IApprovalService"/> (Laravel when implemented).
/// </summary>
public partial class ProposalDetailsPage : ContentPage
{
	private readonly AppSessionService _session;
	private readonly IProposalService _proposalService;
	private readonly IApprovalService _approvalService;
	private readonly IRevisionService _revisionService;
	private Proposal? currentProposal;
	private List<ApprovalStep> approvalSteps = [];

	public ProposalDetailsPage(
		AppSessionService session,
		IProposalService proposalService,
		IApprovalService approvalService,
		IRevisionService revisionService)
	{
		InitializeComponent();
		_session = session;
		_proposalService = proposalService;
		_approvalService = approvalService;
		_revisionService = revisionService;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await LoadProposalDetailsAsync();
	}

	private async Task LoadProposalDetailsAsync()
	{
		currentProposal = _session.SelectedProposal;
		if (currentProposal is null)
		{
			await DisplayAlertAsync(
				"No proposal selected",
				"Go to Pending Approvals, choose a proposal, then tap “Open proposal details.”",
				"OK");
			await Shell.Current.GoToAsync("//pendingapprovals");
			return;
		}

		var refreshed = await _proposalService.GetProposalByIdAsync(currentProposal.Id);
		if (refreshed is not null)
		{
			currentProposal = refreshed;
			_session.SetSelectedProposal(refreshed);
		}

		var currentUser = _session.CurrentUser;
		approvalSteps = _approvalService.BuildApprovalSteps(currentProposal).ToList();
		TitleLabel.Text = currentProposal.Title;
		OrganizationLabel.Text = currentProposal.OrganizationName;
		var flowLabel = ProposalWorkflowService.GetEventTypeDisplay(currentProposal.ApprovalFlowType);
		SummaryLineLabel.Text = $"{flowLabel} · {currentProposal.Status} · Stage: {currentProposal.CurrentStage}";

		EventTypeLabel.Text = $"Event type: {flowLabel}";
		FlowHelperLabel.Text = ProposalWorkflowService.GetCompactWorkflowNote(currentProposal.ApprovalFlowType);
		StatusLabel.Text = currentProposal.Status;
		ApplyStatusBadgeStyle(currentProposal.Status);
		StageHeroLabel.Text = $"Current stage: {currentProposal.CurrentStage}";
		SubmittedByLabel.Text = $"Submitted by: {currentProposal.SubmittedBy}";
		ActivityDateLabel.Text = currentProposal.ActivityDate.ToString("MMMM dd, yyyy");
		VenueLabel.Text = currentProposal.Venue;
		BudgetLabel.Text = $"PHP {currentProposal.Budget:N2}";
		CurrentStageLabel.Text = currentProposal.CurrentStage;
		DescriptionLabel.Text = currentProposal.Description;

		var isFullyApproved = string.Equals(currentProposal.Status, "Fully Approved", StringComparison.OrdinalIgnoreCase);
		var isReturned = string.Equals(currentProposal.Status, "Returned for Revision", StringComparison.OrdinalIgnoreCase);

		PurposeHintLabel.IsVisible = isFullyApproved;
		PurposeHintLabel.Text = isFullyApproved ? "Fully approved — forms below are for your records." : string.Empty;

		MyRoleLabel.Text = $"Role: {currentUser?.Role ?? "—"}";

		ActivityRequestBtn.IsVisible = true;
		ProposalFormViewBtn.IsVisible = true;

		DigitalApprovalBanner.IsVisible = isFullyApproved;
		if (isFullyApproved)
		{
			ApprovalTimestampLabel.Text = currentProposal.FullyApprovedAt.HasValue
				? $"Approved on {currentProposal.FullyApprovedAt:MMMM dd, yyyy}"
				: "Approved (date pending from server).";
		}

		ApprovalRules.ApplyWorkflowPermissions(currentProposal, currentUser);
		var canEdit = !isFullyApproved && ApprovalRules.CanEdit(currentUser, currentProposal);
		var canApprove = !isFullyApproved && ApprovalRules.CanApprove(currentUser, currentProposal);

		var roleName = currentUser?.Role ?? "Signed out";
		ReviewerContextLabel.Text = BuildReviewerContextLine(currentProposal);
		YourAccessLabel.Text = BuildYourAccessLine(roleName, canEdit, canApprove, isFullyApproved);

		EditBtn.IsVisible = canEdit;
		EditBtn.IsEnabled = canEdit;
		EditBtn.Opacity = canEdit ? 1 : 0.45;

		var isPresident = string.Equals(currentUser?.Role, "RSO President", StringComparison.OrdinalIgnoreCase);
		ResubmitBtn.IsVisible = isPresident && isReturned;

		ApproveBtn.IsVisible = canApprove;
		ReturnBtn.IsVisible = canApprove;
		ApproveBtn.IsEnabled = canApprove;
		ApproveBtn.Opacity = canApprove ? 1 : 0.45;
		ReturnBtn.IsEnabled = canApprove;
		ReturnBtn.Opacity = canApprove ? 1 : 0.45;

		SignOffLockedHintLabel.IsVisible = !canApprove && !isFullyApproved;
		SignOffLockedHintLabel.Text = BuildSignOffLockedExplanation(currentProposal, roleName);

		EditRuleMessageLabel.Text = isFullyApproved
			? "Fully approved — editing is closed."
			: canEdit
				? "You can edit as president or current reviewer."
				: BuildLockExplanation(currentProposal, roleName, canEdit: false);

		ReturnedRemarksPanel.IsVisible = isReturned && !string.IsNullOrWhiteSpace(currentProposal.LastRemarks);
		ReturnedRemarksBodyLabel.Text = isReturned && !string.IsNullOrWhiteSpace(currentProposal.LastRemarks)
			? currentProposal.LastRemarks!.Trim()
			: string.Empty;

		WhatNextLabel.Text = BuildWhatNextSummary(canEdit, canApprove, isFullyApproved, isReturned, currentProposal);

		await ApplyRevisionContextAsync(currentProposal.Id);

		AttachmentsHintLabel.Text = "No attachments listed yet.";

		DisplayApprovalProgress();
	}

	private async Task ApplyRevisionContextAsync(int proposalId)
	{
		var revisions = (await _revisionService.GetRevisionHistoryAsync(proposalId)).OrderByDescending(r => r.Timestamp).ToList();
		if (revisions.Count == 0)
		{
			LastUpdatedLabel.Text = "No revision history yet.";
			LatestChangesLabel.Text = "No recent changes logged.";
			return;
		}

		var latest = revisions[0];
		LastUpdatedLabel.Text =
			$"Last edit: {latest.EditedBy} · {latest.Timestamp:MMM dd, yyyy}";

		var bullets = revisions.Take(3)
			.Select(r => $"• {FormatRevisionLine(r)}")
			.ToList();
		LatestChangesLabel.Text = string.Join('\n', bullets);
	}

	private static string FormatRevisionLine(RevisionLog r)
	{
		var field = string.IsNullOrWhiteSpace(r.FieldChanged) ? "Record" : r.FieldChanged;
		return $"{field} updated — {r.Timestamp:MMM dd}";
	}

	private static string BuildWhatNextSummary(bool canEdit, bool canApprove, bool isFullyApproved, bool isReturned, Proposal proposal)
	{
		if (isFullyApproved)
		{
			return "View details and forms; no action required.";
		}

		if (canApprove)
		{
			return isReturned
				? "Approve, return with remarks, or check forms for updates."
				: "Review details, open forms if needed, then approve or return.";
		}

		if (canEdit)
		{
			return "Update the proposal form; sign-off stays with the current reviewer.";
		}

		return $"With {proposal.CurrentStage} now — you have read-only access.";
	}

	private static string BuildSignOffLockedExplanation(Proposal proposal, string roleName)
	{
		if (string.Equals(proposal.Status, "Fully Approved", StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}

		return $"Sign-off is for the current stage only ({proposal.CurrentStage}). Signed in as {roleName}.";
	}

	private static string BuildLockExplanation(Proposal proposal, string roleName, bool canEdit)
	{
		if (canEdit || string.Equals(proposal.Status, "Fully Approved", StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}

		return $"Editing is with {proposal.CurrentStage} now (signed in as {roleName}).";
	}

	private static string BuildReviewerContextLine(Proposal proposal)
	{
		if (string.Equals(proposal.Status, "Fully Approved", StringComparison.OrdinalIgnoreCase))
		{
			return "Workflow complete.";
		}

		if (string.Equals(proposal.Status, "Returned for Revision", StringComparison.OrdinalIgnoreCase))
		{
			return $"Returned — president may edit; resubmit sends it back to {ProposalWorkflowService.GetStages(proposal.ApprovalFlowType)[0]}.";
		}

		return $"Current stage: {proposal.CurrentStage}";
	}

	private static string BuildYourAccessLine(string roleName, bool canEdit, bool canApprove, bool isFullyApproved)
	{
		if (isFullyApproved)
		{
			return $"View only · {roleName}";
		}

		if (canApprove && canEdit)
		{
			return $"Edit & sign-off · {roleName}";
		}

		if (canApprove)
		{
			return $"Can approve or return · {roleName}";
		}

		if (canEdit)
		{
			return $"Can edit · {roleName}";
		}

		return $"View only · {roleName}";
	}

	private void ApplyStatusBadgeStyle(string status)
	{
		var s = status.Trim();
		var (bg, fg) = s switch
		{
			"Under Review" => (UiBrand.NavyWash, UiBrand.Navy),
			"Returned for Revision" => (UiBrand.Navy, Colors.White),
			"Fully Approved" => (UiBrand.Navy, Colors.White),
			"Submitted" => (Colors.White, UiBrand.Navy),
			"Draft" => (Colors.White, UiBrand.NavyDeep),
			_ => (UiBrand.NavyWash, UiBrand.Navy)
		};

		StatusBadgeBorder.BackgroundColor = bg;
		StatusLabel.TextColor = fg;
	}

	private void DisplayApprovalProgress()
	{
		ApprovalStepsStack.Children.Clear();

		foreach (var step in approvalSteps)
		{
			var stepCard = CreateApprovalStepCard(step);
			ApprovalStepsStack.Children.Add(stepCard);
		}
	}

	private Frame CreateApprovalStepCard(ApprovalStep step)
	{
		var (backgroundColor, borderColor, statusColor, statusIcon) = step.Status switch
		{
			"Completed" => (UiBrand.SuccessLight, UiBrand.SuccessBorder, UiBrand.Success, "Done"),
			"Current" => (UiBrand.NavyWash, UiBrand.NavyLine, UiBrand.Navy, "Current"),
			"Current (Returned)" => (UiBrand.NavyWash, UiBrand.Navy, UiBrand.NavyDeep, "Returned"),
			"Pending" => (UiBrand.White, UiBrand.NavyLine, UiBrand.NavyMutedText, "Waiting"),
			"Locked" => (UiBrand.White, UiBrand.NavyLine, UiBrand.NavyMutedText, "Locked"),
			_ => (UiBrand.White, UiBrand.NavyLine, UiBrand.NavyMutedText, "—")
		};

		var signatoryLabel = step.Status switch
		{
			"Completed" => "Approved at this step",
			"Current" => "Action required here",
			"Current (Returned)" => "Returned — action here",
			"Pending" => "Waiting on earlier steps",
			"Locked" => "Not yet reached",
			_ => "—"
		};

		var titleColor = step.Status == "Completed" ? UiBrand.SuccessText : UiBrand.Navy;
		var subtitleColor = step.Status == "Completed" ? UiBrand.SuccessSubtext : UiBrand.NavyMutedText;

		return new Frame
		{
			CornerRadius = 12,
			BorderColor = borderColor,
			HasShadow = false,
			Padding = 12,
			BackgroundColor = backgroundColor,
			Content = new HorizontalStackLayout
			{
				Spacing = 12,
				Children =
				{
					new VerticalStackLayout
					{
						Spacing = 2,
						HorizontalOptions = LayoutOptions.Fill,
						Children =
						{
							new Label
							{
								Text = step.RoleName,
								FontSize = 13,
								FontAttributes = FontAttributes.Bold,
								TextColor = titleColor
							},
							new Label
							{
								Text = signatoryLabel,
								FontSize = 11,
								TextColor = subtitleColor,
								FontAttributes = step.Status == "Completed" ? FontAttributes.Italic : FontAttributes.None
							},
							new Label
							{
								Text = statusIcon,
								FontSize = 11,
								TextColor = statusColor,
								FontAttributes = FontAttributes.Bold,
								Margin = new Thickness(0, 2, 0, 0)
							}
						}
					}
				}
			}
		};
	}

	private async void OnApproveClicked(object? sender, EventArgs e)
	{
		if (currentProposal is null)
		{
			return;
		}

		var result = await _approvalService.ApproveProposalAsync(currentProposal.Id);
		if (!result.Success)
		{
			await DisplayAlertAsync("Could not approve", result.Message ?? "Please try again.", "OK");
			return;
		}

		// Success feedback: green completed rows in "Approval progress" (no separate banner).
		await LoadProposalDetailsAsync();
	}

	private async void OnReturnClicked(object? sender, EventArgs e)
	{
		if (currentProposal is null)
		{
			return;
		}

		var remarks = RemarksEntry.Text;
		var result = await _approvalService.ReturnProposalAsync(currentProposal.Id, remarks);
		if (!result.Success)
		{
			await DisplayAlertAsync("Could not return", result.Message ?? "Please try again.", "OK");
			return;
		}

		await DisplayAlertAsync(
			"Returned for revision",
			result.Message ?? "The proposal was sent back. The RSO will see your remarks.",
			"OK");
		await LoadProposalDetailsAsync();
	}

	private async void OnViewActivityRequestClicked(object? sender, EventArgs e)
	{
		if (currentProposal is null)
		{
			return;
		}

		await Shell.Current.GoToAsync("activityrequestform");
	}

	private async void OnViewProposalFormClicked(object? sender, EventArgs e)
	{
		if (currentProposal is null)
		{
			return;
		}

		_session.PrepareProposalFormNavigation(browseOnly: true);
		await Shell.Current.GoToAsync("proposalform");
	}

	private async void OnEditClicked(object? sender, EventArgs e)
	{
		if (!CanCurrentUserEdit())
		{
			await DisplayAlertAsync(
				"Editing not available",
				"You cannot edit this proposal at this stage. See the note above the remarks box.",
				"OK");
			return;
		}

		_session.PrepareProposalFormNavigation(browseOnly: false);
		await Shell.Current.GoToAsync("proposalform");
	}

	private async void OnViewHistoryClicked(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("revisionhistory");
	}

	private async void OnResubmitForReviewClicked(object? sender, EventArgs e)
	{
		if (currentProposal is null)
		{
			return;
		}

		if (_session.CurrentUser is null ||
		    !string.Equals(_session.CurrentUser.Role, "RSO President", StringComparison.OrdinalIgnoreCase))
		{
			await DisplayAlertAsync("Not available", "Only the RSO President can resubmit a proposal for review.", "OK");
			return;
		}

		if (!string.Equals(currentProposal.Status, "Returned for Revision", StringComparison.OrdinalIgnoreCase))
		{
			await DisplayAlertAsync("Not needed", "Resubmit is only for proposals that are returned for revision.", "OK");
			return;
		}

		var firstStage = ProposalWorkflowService.GetStages(currentProposal.ApprovalFlowType)[0];
		currentProposal.Status = "Under Review";
		currentProposal.CurrentStage = firstStage;
		var result = await _proposalService.UpdateProposalAsync(currentProposal);
		if (!result.Success)
		{
			await DisplayAlertAsync("Could not resubmit", result.Message ?? "Please try again.", "OK");
			return;
		}

		await DisplayAlertAsync(
			"Resubmitted for review",
			$"The proposal is back at {firstStage}. Signatories can act again in order.",
			"OK");
		await LoadProposalDetailsAsync();
	}

	private bool CanCurrentUserEdit()
	{
		if (currentProposal is null)
		{
			return false;
		}

		ApprovalRules.ApplyWorkflowPermissions(currentProposal, _session.CurrentUser);
		return ApprovalRules.CanEdit(_session.CurrentUser, currentProposal);
	}
}
