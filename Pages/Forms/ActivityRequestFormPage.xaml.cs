namespace docusystem.Pages.Forms;

using System.Globalization;
using docusystem.Models;
using docusystem.Services;

/// <summary>
/// Activity Request Form — opened from Proposal Details for the proposal in <see cref="AppSessionService.SelectedProposal"/>.
/// </summary>
public partial class ActivityRequestFormPage : ContentPage
{
	private readonly AppSessionService _session;

	public ActivityRequestFormPage(AppSessionService session)
	{
		InitializeComponent();
		_session = session;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		var proposal = _session.SelectedProposal;
		if (proposal is null)
		{
			await DisplayAlertAsync(
				"Notice",
				"Select a proposal from Pending Approvals first, then open this form from Proposal Details.",
				"OK");
			await Shell.Current.GoToAsync("//pendingapprovals");
			return;
		}

		ApplyProposalContext(proposal);
		ApplyEditingPolicy(proposal);
	}

	private void ApplyProposalContext(Proposal proposal)
	{
		Title = "Activity request";
		FormContextSubtitleLabel.Text =
			$"{proposal.OrganizationName} · {proposal.Title}";
		RsoNameEntry.Text = proposal.OrganizationName;
		ActivityTitleEntry.Text = proposal.Title;
		VenueEntry.Text = proposal.Venue;
		ProposedBudgetEntry.Text = proposal.Budget.ToString("0.##", CultureInfo.InvariantCulture);
		ActivityDatePicker.Date = proposal.ActivityDate.Date;
	}

	private void ApplyEditingPolicy(Proposal proposal)
	{
		ApprovalRules.ApplyWorkflowPermissions(proposal, _session.CurrentUser);
		var canEdit = ApprovalRules.CanEdit(_session.CurrentUser, proposal);
		var role = _session.CurrentUser?.Role ?? "User";
		ActivityAccessHintLabel.Text = canEdit
			? $"You can edit ({role})."
			: $"Read-only ({role}).";

		void SetEnabled(bool on, params View[] views)
		{
			foreach (var v in views)
			{
				v.IsEnabled = on;
			}
		}

		SetEnabled(canEdit,
			RsoNameEntry,
			ActivityTitleEntry,
			NatureCoCurricularCheck,
			NatureNonCurricularCheck,
			NatureCommunityExtensionCheck,
			NatureOthersCheck,
			NatureOthersSpecifyEntry,
			TypeSeminarCheck,
			TypeGeneralAssemblyCheck,
			TypeOrientationCheck,
			TypeCompetitionCheck,
			TypeRecruitmentCheck,
			TypeDonationDriveCheck,
			TypeOutreachDonationCheck,
			TypeFundraisingCheck,
			TypeOffCampusCheck,
			TypeOthersCheck,
			TypeOthersSpecifyEntry,
			PartnerOrganizationsEditor,
			TargetSdgEntry,
			ProposedBudgetEntry,
			BudgetSourceEntry,
			ActivityDatePicker,
			VenueEntry,
			RevisionNotesEditor);

		SaveDraftButton.IsEnabled = canEdit;
		SubmitButton.IsEnabled = canEdit;
		SaveDraftButton.Opacity = canEdit ? 1 : 0.5;
		SubmitButton.Opacity = canEdit ? 1 : 0.5;
	}

	private void OnNatureOthersChanged(object? sender, CheckedChangedEventArgs e)
	{
		NatureOthersSpecLayout.IsVisible = NatureOthersCheck.IsChecked;
		if (!NatureOthersCheck.IsChecked)
		{
			NatureOthersSpecifyEntry.Text = string.Empty;
		}
	}

	private void OnTypeOthersChanged(object? sender, CheckedChangedEventArgs e)
	{
		TypeOthersSpecLayout.IsVisible = TypeOthersCheck.IsChecked;
		if (!TypeOthersCheck.IsChecked)
		{
			TypeOthersSpecifyEntry.Text = string.Empty;
		}
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

	/// <summary>Placeholder — wire to local draft store / API later.</summary>
	private async void OnSaveDraftClicked(object? sender, EventArgs e)
	{
		ClearValidationHints();
		await DisplayAlertAsync("Save Draft", "Draft save will connect to the Laravel API later.", "OK");
	}

	/// <summary>Placeholder validation only; no backend submit.</summary>
	private async void OnSubmitClicked(object? sender, EventArgs e)
	{
		ClearValidationHints();
		var ok = true;
		if (string.IsNullOrWhiteSpace(RsoNameEntry.Text))
		{
			SetValidation(RsoNameValidationLabel, "Name of RSO is required.");
			ok = false;
		}

		if (string.IsNullOrWhiteSpace(ActivityTitleEntry.Text))
		{
			SetValidation(ActivityTitleValidationLabel, "Title of Activity is required.");
			ok = false;
		}

		var anyNature = NatureCoCurricularCheck.IsChecked
			|| NatureNonCurricularCheck.IsChecked
			|| NatureCommunityExtensionCheck.IsChecked
			|| NatureOthersCheck.IsChecked;
		if (!anyNature)
		{
			SetValidation(NatureActivityValidationLabel, "Select at least one nature of activity.");
			ok = false;
		}
		else if (NatureOthersCheck.IsChecked && string.IsNullOrWhiteSpace(NatureOthersSpecifyEntry.Text))
		{
			SetValidation(NatureActivityValidationLabel, "Please specify when \"Others\" is selected under Nature of Activity.");
			ok = false;
		}

		var anyType = TypeSeminarCheck.IsChecked
			|| TypeGeneralAssemblyCheck.IsChecked
			|| TypeOrientationCheck.IsChecked
			|| TypeCompetitionCheck.IsChecked
			|| TypeRecruitmentCheck.IsChecked
			|| TypeDonationDriveCheck.IsChecked
			|| TypeOutreachDonationCheck.IsChecked
			|| TypeFundraisingCheck.IsChecked
			|| TypeOffCampusCheck.IsChecked
			|| TypeOthersCheck.IsChecked;
		if (!anyType)
		{
			SetValidation(TypeActivityValidationLabel, "Select at least one type of activity.");
			ok = false;
		}
		else if (TypeOthersCheck.IsChecked && string.IsNullOrWhiteSpace(TypeOthersSpecifyEntry.Text))
		{
			SetValidation(TypeActivityValidationLabel, "Please specify when \"Others\" is selected under Type of Activity.");
			ok = false;
		}

		if (string.IsNullOrWhiteSpace(ProposedBudgetEntry.Text))
		{
			SetValidation(BudgetValidationLabel, "Proposed budget is required.");
			ok = false;
		}

		if (string.IsNullOrWhiteSpace(VenueEntry.Text))
		{
			SetValidation(VenueValidationLabel, "Venue is required.");
			ok = false;
		}

		if (!ok)
		{
			await DisplayAlertAsync("Validation", "Please correct the fields highlighted below.", "OK");
			return;
		}

		await DisplayAlertAsync("Submit", "Submit will POST to Laravel when the API is integrated.", "OK");
	}

	private void ClearValidationHints()
	{
		SetValidation(RsoNameValidationLabel, null);
		SetValidation(ActivityTitleValidationLabel, null);
		SetValidation(NatureActivityValidationLabel, null);
		SetValidation(TypeActivityValidationLabel, null);
		SetValidation(BudgetValidationLabel, null);
		SetValidation(VenueValidationLabel, null);
	}
}
