namespace docusystem.Pages.Forms;

using System.Globalization;
using docusystem.Models;
using docusystem.Services;

/// <summary>
/// Full proposal form — opens from Proposal Details for the proposal in <see cref="AppSessionService.SelectedProposal"/>.
/// </summary>
public partial class ProposalFormPage : ContentPage
{
	private readonly AppSessionService _session;
	private readonly IProposalService _proposalService;
	private Proposal? _loadedProposal;
	private string? _baselineSnapshot;

	public ProposalFormPage(AppSessionService session, IProposalService proposalService)
	{
		InitializeComponent();
		_session = session;
		_proposalService = proposalService;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		var browseOnly = _session.TryConsumeProposalFormBrowseOnly();
		_loadedProposal = _session.SelectedProposal;

		// Forms are only opened in context from Proposal Details (with a selected proposal).
		if (_loadedProposal is null)
		{
			await DisplayAlertAsync(
				"Notice",
				"Select a proposal from Pending Approvals first, then open forms from Proposal Details.",
				"OK");
			await Shell.Current.GoToAsync("//pendingapprovals");
			return;
		}

		ApprovalRules.ApplyWorkflowPermissions(_loadedProposal, _session.CurrentUser);
		PopulateFormFromProposal(_loadedProposal);
		var user = _session.CurrentUser;
		var canEdit = ApprovalRules.CanEdit(user, _loadedProposal);
		if (browseOnly)
		{
			canEdit = false;
		}

		Title = canEdit ? "Edit proposal" : "View proposal form";
		FormContextSubtitleLabel.Text =
			$"{_loadedProposal.OrganizationName} · {_loadedProposal.Title} · {_loadedProposal.CurrentStage}";

		var role = user?.Role ?? "User";
		ProposalFormAccessHintLabel.Text = browseOnly
			? $"Read-only ({role}). Use Edit on proposal details to change fields."
			: canEdit
				? $"You can edit ({role})."
				: $"Read-only ({role}).";

		// Approval actions belong on Proposal Details, not this form
		ReturnRevisionButton.IsVisible = false;
		ApproveButton.IsVisible = false;

		ApplyEditingPolicy(canEdit, hasProposal: true);
		_baselineSnapshot = SnapshotForm();
	}

	private string SnapshotForm() =>
		string.Join("|",
			OrganizationNameEntry.Text,
			SchoolEntry.Text,
			DepartmentProgramEntry.Text,
			AcademicYearEntry.Text,
			ProjectTitleEntry.Text,
			ProposedDatesEditor.Text,
			ProposedTimeEntry.Text,
			VenueEntry.Text,
			MainObjectiveEditor.Text,
			SpecificObjectivesEditor.Text,
			CriteriaEditor.Text,
			ProgramFlowEditor.Text,
			ProposedBudgetEntry.Text,
			FundingSourceEntry.Text,
			ExpenseMaterialsEntry.Text,
			ExpenseFoodEntry.Text,
			ExpenseOtherEntry.Text,
			ResourcePersonResumeEditor.Text,
			ResponsiblePersonsEditor.Text,
			RevisionNotesEditor.Text);

	private bool HasUnsavedChanges() =>
		_baselineSnapshot is not null && SnapshotForm() != _baselineSnapshot;

	private void ClearFormFields()
	{
		OrganizationNameEntry.Text = string.Empty;
		SchoolEntry.Text = string.Empty;
		DepartmentProgramEntry.Text = string.Empty;
		AcademicYearEntry.Text = string.Empty;
		ProjectTitleEntry.Text = string.Empty;
		ProposedDatesEditor.Text = string.Empty;
		ProposedTimeEntry.Text = string.Empty;
		VenueEntry.Text = string.Empty;
		MainObjectiveEditor.Text = string.Empty;
		SpecificObjectivesEditor.Text = string.Empty;
		CriteriaEditor.Text = string.Empty;
		ProgramFlowEditor.Text = string.Empty;
		ProposedBudgetEntry.Text = string.Empty;
		FundingSourceEntry.Text = string.Empty;
		ExpenseMaterialsEntry.Text = string.Empty;
		ExpenseFoodEntry.Text = string.Empty;
		ExpenseOtherEntry.Text = string.Empty;
		ResourcePersonResumeEditor.Text = string.Empty;
		ResponsiblePersonsEditor.Text = string.Empty;
		RevisionNotesEditor.Text = string.Empty;
	}

	private void PopulateFormFromProposal(Proposal p)
	{
		OrganizationNameEntry.Text = p.OrganizationName;
		ProjectTitleEntry.Text = p.Title;
		ProposedDatesEditor.Text = p.ActivityDate.ToString("MMMM dd, yyyy", CultureInfo.CurrentCulture);
		VenueEntry.Text = p.Venue;
		ProposedBudgetEntry.Text = p.Budget.ToString("0.##", CultureInfo.InvariantCulture);
		MainObjectiveEditor.Text = p.Description;
	}

	private void ApplyEditingPolicy(bool canEdit, bool hasProposal)
	{
		void SetEditable(bool on, params InputView[] views)
		{
			foreach (var v in views)
			{
				v.IsEnabled = on;
			}
		}

		var on = canEdit || !hasProposal;
		SetEditable(on,
			OrganizationNameEntry,
			SchoolEntry,
			DepartmentProgramEntry,
			AcademicYearEntry,
			ProjectTitleEntry,
			ProposedDatesEditor,
			ProposedTimeEntry,
			VenueEntry,
			MainObjectiveEditor,
			SpecificObjectivesEditor,
			CriteriaEditor,
			ProgramFlowEditor,
			ProposedBudgetEntry,
			FundingSourceEntry,
			ExpenseMaterialsEntry,
			ExpenseFoodEntry,
			ExpenseOtherEntry,
			ResourcePersonResumeEditor,
			ResponsiblePersonsEditor,
			RevisionNotesEditor);

		SaveChangesButton.IsEnabled = hasProposal ? canEdit : false;
		SaveChangesButton.Opacity = SaveChangesButton.IsEnabled ? 1 : 0.5;
		SaveDraftButton.IsEnabled = on;
		SubmitButton.IsEnabled = on;
		CancelEditButton.IsEnabled = true;
	}

	private static void SetValidation(Label label, string? message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			label.Text = string.Empty;
			label.IsVisible = false;
		}
		else
		{
			label.Text = message;
			label.IsVisible = true;
		}
	}

	private void ClearValidationHints()
	{
		SetValidation(OrganizationNameValidationLabel, null);
		SetValidation(ProjectTitleValidationLabel, null);
		SetValidation(BudgetValidationLabel, null);
	}

	private async void OnCancelEditClicked(object? sender, EventArgs e)
	{
		if (_loadedProposal is null)
		{
			await Shell.Current.GoToAsync("//pendingapprovals");
			return;
		}

		if (HasUnsavedChanges())
		{
			var leave = await DisplayAlertAsync(
				"Unsaved changes",
				"You edited this form but have not saved. Leave without saving?",
				"Leave without saving",
				"Keep editing");
			if (!leave)
			{
				return;
			}
		}

		await Shell.Current.GoToAsync("..");
	}

	private async void OnSaveDraftClicked(object? sender, EventArgs e)
	{
		if (_loadedProposal is null)
		{
			await DisplayAlertAsync("Save Draft", "Open this form from Proposal Details after selecting a proposal.", "OK");
			return;
		}

		await DisplayAlertAsync("Save Draft", "TODO: connect to Laravel draft endpoint when available.", "OK");
	}

	private async void OnSaveChangesClicked(object? sender, EventArgs e)
	{
		if (_loadedProposal is null)
		{
			await DisplayAlertAsync("Save", "Select a proposal and open this form from Proposal Details before saving.", "OK");
			return;
		}

		ClearValidationHints();

		if (string.IsNullOrWhiteSpace(OrganizationNameEntry.Text))
		{
			SetValidation(OrganizationNameValidationLabel, "Name of Organization is required.");
			await DisplayAlertAsync("Validation", "Please complete the required fields.", "OK");
			return;
		}

		if (string.IsNullOrWhiteSpace(ProjectTitleEntry.Text))
		{
			SetValidation(ProjectTitleValidationLabel, "Project / Activity Title is required.");
			await DisplayAlertAsync("Validation", "Please complete the required fields.", "OK");
			return;
		}

		if (string.IsNullOrWhiteSpace(VenueEntry.Text))
		{
			await DisplayAlertAsync("Validation Error", "Venue is required.", "OK");
			return;
		}

		if (string.IsNullOrWhiteSpace(ProposedBudgetEntry.Text))
		{
			SetValidation(BudgetValidationLabel, "Proposed budget is required.");
			await DisplayAlertAsync("Validation", "Please complete the required fields.", "OK");
			return;
		}

		if (!DateTime.TryParse(ProposedDatesEditor.Text, out var activityDate))
		{
			await DisplayAlertAsync("Validation Error", "Please enter a valid date for Proposed Date(s).", "OK");
			return;
		}

		if (!decimal.TryParse(ProposedBudgetEntry.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var budget) &&
		    !decimal.TryParse(ProposedBudgetEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out budget))
		{
			await DisplayAlertAsync("Validation Error", "Please enter a valid numeric budget.", "OK");
			return;
		}

		var payload = CloneProposal(_loadedProposal);
		payload.LastRemarks = _loadedProposal.LastRemarks;
		payload.OrganizationName = OrganizationNameEntry.Text.Trim();
		payload.Title = ProjectTitleEntry.Text.Trim();
		payload.ActivityDate = activityDate;
		payload.Venue = VenueEntry.Text.Trim();
		payload.Budget = budget;
		payload.Description = MainObjectiveEditor.Text?.Trim() ?? string.Empty;

		var result = await _proposalService.UpdateProposalAsync(payload);
		if (!result.Success)
		{
			await DisplayAlertAsync("Save", result.Message ?? "Could not save changes.", "OK");
			return;
		}

		CopyProposal(payload, _loadedProposal);
		_session.SetSelectedProposal(_loadedProposal);
		_baselineSnapshot = SnapshotForm();

		await DisplayAlertAsync(
			"Changes saved",
			result.Message ?? "Your updates were saved. Returning to Proposal Details.",
			"OK");
		await Shell.Current.GoToAsync("..");
	}

	private async void OnSubmitClicked(object? sender, EventArgs e)
	{
		ClearValidationHints();
		var ok = true;
		if (string.IsNullOrWhiteSpace(OrganizationNameEntry.Text))
		{
			SetValidation(OrganizationNameValidationLabel, "Name of Organization is required.");
			ok = false;
		}

		if (string.IsNullOrWhiteSpace(ProjectTitleEntry.Text))
		{
			SetValidation(ProjectTitleValidationLabel, "Project / Activity Title is required.");
			ok = false;
		}

		if (string.IsNullOrWhiteSpace(ProposedBudgetEntry.Text))
		{
			SetValidation(BudgetValidationLabel, "Proposed budget is required.");
			ok = false;
		}

		if (!ok)
		{
			await DisplayAlertAsync("Validation", "Please complete the required fields.", "OK");
			return;
		}

		await DisplayAlertAsync("Submit", "TODO: submit workflow via Laravel API when integrated.", "OK");
	}

	private async void OnReturnForRevisionClicked(object? sender, EventArgs e)
	{
		await DisplayAlertAsync("Return for Revision", "Use Proposal Details for this action.", "OK");
	}

	private async void OnApproveClicked(object? sender, EventArgs e)
	{
		await DisplayAlertAsync("Approve", "Use Proposal Details for this action.", "OK");
	}

	private static Proposal CloneProposal(Proposal source) =>
		new()
		{
			Id = source.Id,
			Title = source.Title,
			OrganizationName = source.OrganizationName,
			SubmittedBy = source.SubmittedBy,
			CurrentStage = source.CurrentStage,
			Status = source.Status,
			ActivityDate = source.ActivityDate,
			Venue = source.Venue,
			Budget = source.Budget,
			Description = source.Description,
			CanEdit = source.CanEdit,
			CanApprove = source.CanApprove,
			SubmittedDate = source.SubmittedDate,
			FullyApprovedAt = source.FullyApprovedAt,
			LastRemarks = source.LastRemarks
		};

	private static void CopyProposal(Proposal from, Proposal to)
	{
		to.Title = from.Title;
		to.OrganizationName = from.OrganizationName;
		to.SubmittedBy = from.SubmittedBy;
		to.CurrentStage = from.CurrentStage;
		to.Status = from.Status;
		to.ActivityDate = from.ActivityDate;
		to.Venue = from.Venue;
		to.Budget = from.Budget;
		to.Description = from.Description;
		to.CanEdit = from.CanEdit;
		to.CanApprove = from.CanApprove;
		to.SubmittedDate = from.SubmittedDate;
		to.FullyApprovedAt = from.FullyApprovedAt;
		to.LastRemarks = from.LastRemarks;
	}
}
