namespace docusystem.Pages.Approvals;

using System.Text.Json;
using docusystem.Models;
using docusystem.Services;

/// <summary>
/// Proposal details with section-based field review and approval workflow.
/// Approver marks each field as Passed or Revision (with note), then submits.
/// </summary>
public partial class ProposalDetailsPage : ContentPage
{
	private readonly AppSessionService _session;
	private readonly IAuthService _authService;
	private readonly IProposalService _proposalService;
	private readonly IApprovalService _approvalService;
	private readonly IRevisionService _revisionService;
	private readonly IAttachmentService _attachmentService;

	private Proposal? _proposal;
	private List<ApprovalStep> _steps = [];
	private List<ProposalFieldReview> _fields = [];
	private List<ProposalAttachment> _attachments = [];
	private List<RevisionLog> _historyEntries = [];
	private CancellationTokenSource? _fieldReviewAutoSaveCts;
	private bool _fieldReviewAutoSaveInFlight;
	private bool _showFieldReviewControls;
	private bool _canInteractFieldReviewControls;

	// Workflow stage selected in the horizontal track
	private int _selectedStepIndex;

	// Colors
	private static readonly Color PassedBg = Color.FromArgb("#E8F5EF");
	private static readonly Color PassedBorder = Color.FromArgb("#3CB371");
	private static readonly Color PassedText = Color.FromArgb("#1A7A45");
	private static readonly Color RevisionBg = Color.FromArgb("#FFF4E6");
	private static readonly Color RevisionBorder = Color.FromArgb("#E08030");
	private static readonly Color RevisionText = Color.FromArgb("#C06000");
	private static readonly Color PendingBorder = Color.FromArgb("#E0E4ED");
	private static readonly Color PendingText = Color.FromArgb("#5A6A8A");
	private static readonly Color PrimaryColor = Color.FromArgb("#003087");

	public ProposalDetailsPage(
		AppSessionService session,
		IAuthService authService,
		IProposalService proposalService,
		IApprovalService approvalService,
		IRevisionService revisionService,
		IAttachmentService attachmentService)
	{
		InitializeComponent();
		_session = session;
		_authService = authService;
		_proposalService = proposalService;
		_approvalService = approvalService;
		_revisionService = revisionService;
		_attachmentService = attachmentService;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await LoadAsync();
	}

	// ──────────────────────────────────────────────────────────────────────────
	// Load
	// ──────────────────────────────────────────────────────────────────────────

	private async Task LoadAsync()
	{
		// Refresh `/api/user` so role_id matches Laravel auth before Passed/Revision / approve logic.
		// Cold-start restores JSON from SecureStorage without hitting AuthService normalization merges.
		try
		{
			await _authService.GetCurrentUserAsync().ConfigureAwait(true);
		}
		catch (Exception)
		{
			// Offline — continue with cached session user.
		}

		var selectedSnapshot = _session.SelectedProposal;
		_proposal = selectedSnapshot;
		if (_proposal is null)
		{
			await DisplayAlertAsync(
				"No proposal selected",
				"Go to Pending Approvals and open a proposal to review real data.",
				"OK");
			await Shell.Current.GoToAsync("//pendingapprovals");
			return;
		}

		var refreshed = await _proposalService.GetProposalByIdAsync(_proposal.Id);
		if (refreshed is not null)
		{
			// Pending list payload often carries the most accurate current-stage label for the
			// logged-in reviewer. Some detail responses return only numeric current step (e.g. 1),
			// which can temporarily hide/disable Passed/Revision buttons on mobile.
			if (selectedSnapshot is not null)
			{
				if (string.IsNullOrWhiteSpace(refreshed.CurrentStage) || IsNumericStageToken(refreshed.CurrentStage))
				{
					refreshed.CurrentStage = selectedSnapshot.CurrentStage;
				}

				if (string.IsNullOrWhiteSpace(refreshed.Status))
				{
					refreshed.Status = selectedSnapshot.Status;
				}
			}

			_proposal = refreshed;
			_session.SetSelectedProposal(refreshed);
		}

		// Recompute flow from real payload wording (Co-curricular / Non-curricular)
		// so stage routing matches backend proposal type.
		_proposal.ApprovalFlowType = ProposalWorkflowService.InferFlowTypeFromProposal(_proposal);

		_steps = _approvalService.BuildApprovalSteps(_proposal).ToList();
		_attachments = await LoadAttachmentsForReviewAsync(_proposal.Id);
		_fields = BuildFieldList(_proposal, _attachments);
		_historyEntries = (await _revisionService.GetRevisionHistoryAsync(_proposal.Id).ConfigureAwait(true))
			.OrderByDescending(h => h.Timestamp)
			.ToList();
		// Default: select the current signatory step (or -1 = Submitted node when no steps yet).
		_selectedStepIndex = FindCurrentStepIndex();

		BindHeader();
		BindSummaryCard();
		BuildFieldCards();
		UpdateProgressBar();
		UpdateReviewSummary();
		RefreshComputedStatusBadge();
		BuildWorkflowTrack();
		BindSelectedStageCard();
		BuildWorkflowLogs();
		UpdateSubmitButtonState();

	}

	// ──────────────────────────────────────────────────────────────────────────
	// Attachments
	// ──────────────────────────────────────────────────────────────────────────

	private async Task<List<ProposalAttachment>> LoadAttachmentsForReviewAsync(int proposalId)
	{
		try
		{
			var list = await _attachmentService.GetAttachmentsAsync(proposalId).ConfigureAwait(true);
			return list.ToList();
		}
		catch (Exception)
		{
			return [];
		}
	}

	private async Task OpenAttachmentAsync(ProposalAttachment att, bool asDownload)
	{
		try
		{
			var url = await ResolveAttachmentUrlAsync(att, asDownload);
			if (string.IsNullOrWhiteSpace(url))
			{
				await DisplayAlertAsync(
					"Cannot open file",
					"This file isn't available right now. The signed link may have expired — please refresh and try again.",
					"OK");
				return;
			}

			if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
			{
				await DisplayAlertAsync("Cannot open file", "The file link is invalid.", "OK");
				return;
			}

			await Launcher.Default.OpenAsync(uri);
		}
		catch (Exception)
		{
			await DisplayAlertAsync(
				"Cannot open file",
				"Something went wrong while opening this file. Please check your connection and try again.",
				"OK");
		}
	}

	private async Task<string?> ResolveAttachmentUrlAsync(ProposalAttachment att, bool asDownload)
	{
		// Prefer URLs the API embedded in the listing — these are typically already signed.
		if (asDownload)
		{
			if (IsAbsoluteUrl(att.DownloadUrl))
			{
				return att.DownloadUrl;
			}
		}
		else
		{
			if (IsAbsoluteUrl(att.StreamUrl))
			{
				return att.StreamUrl;
			}
			if (IsAbsoluteUrl(att.ViewUrl))
			{
				return att.ViewUrl;
			}
		}

		// Fallback: ask the API to mint a fresh signed URL.
		return asDownload
			? await _attachmentService.GetDownloadUrlAsync(att.Id)
			: await _attachmentService.GetViewUrlAsync(att.Id) ?? await _attachmentService.GetStreamUrlAsync(att.Id);
	}

	private static bool IsAbsoluteUrl(string? url) =>
		!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out _);

	private static bool IsNumericStageToken(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		return int.TryParse(value.Trim(), out _);
	}

	private static string FormatFileType(string fileType)
	{
		var trimmed = fileType.Replace('_', ' ').Trim();
		if (string.IsNullOrEmpty(trimmed))
		{
			return string.Empty;
		}
		return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
	}

	private static string FormatBytes(long bytes)
	{
		if (bytes < 1024)
		{
			return $"{bytes} B";
		}
		double kb = bytes / 1024.0;
		if (kb < 1024)
		{
			return $"{kb:N1} KB";
		}
		return $"{kb / 1024.0:N1} MB";
	}

	// ──────────────────────────────────────────────────────────────────────────
	// Header
	// ──────────────────────────────────────────────────────────────────────────

	private void BindHeader()
	{
		if (_proposal is null)
		{
			return;
		}

		var user = _session.CurrentUser;
		MyRoleLabel.Text = $"Role: {user?.Role ?? "—"}";
		TitleLabel.Text = _proposal.Title;
		OrganizationLabel.Text = _proposal.OrganizationName;
		StatusLabel.Text = _proposal.Status;
		StageHeroLabel.Text = $"Stage: {_proposal.CurrentStage}";
		CurrentApproverBadgeLabel.Text = $"Current Approver: {ResolveCurrentApprover()}";

		ApplyStatusBadge(_proposal.Status);

		var isFullyApproved = IsFullyApproved();
		DigitalApprovalBanner.IsVisible = isFullyApproved;
		if (isFullyApproved)
		{
			ApprovalTimestampLabel.Text = _proposal.FullyApprovedAt.HasValue
				? $"Approved on {_proposal.FullyApprovedAt:MMMM dd, yyyy}"
				: "Approved (date pending from server).";
		}

		var isReturned = IsReturned();
		ReturnedRemarksPanel.IsVisible = isReturned && !string.IsNullOrWhiteSpace(_proposal.LastRemarks);
		ReturnedRemarksBodyLabel.Text = _proposal.LastRemarks?.Trim() ?? string.Empty;

		// Approvers-only app mode: resubmit is handled in submitter/web side, not here.
		ResubmitBtn.IsVisible = false;

		ApprovalRules.ApplyWorkflowPermissions(_proposal, user);
		var canApprove = !isFullyApproved && ApprovalRules.CanApprove(user, _proposal);
		var isSignatory = IsAnySignatoryForProposal(user, _proposal);

		// Mobile fail-safe: detail payload sometimes ships without a fully expanded current_step
		// object. If the user is part of the signatory chain, the proposal's current stage matches
		// their role, and status is still actionable, allow interaction. Backend remains the final
		// authority via 403 if the workflow step truly isn't theirs.
		if (!canApprove &&
		    !isFullyApproved &&
		    !IsRsoPresident(user) &&
		    isSignatory &&
		    !string.IsNullOrWhiteSpace(_proposal.CurrentStage) &&
		    IsActionableWorkflowStatus(_proposal.Status) &&
		    DoesUserMatchCurrentStage(user, _proposal))
		{
			canApprove = true;
		}

		// Laravel sometimes emits status values like "approved" (→ Approved) while the proposal is still
		// routed at an intermediate signatory — CurrentStage / synthetic steps already show whose turn it is.
		// Without this, Passed/Revision stay disabled despite correct Stage / Current Approver labels.
		if (!canApprove &&
		    !isFullyApproved &&
		    !IsReturned() &&
		    !IsProposalRejectedForWorkflow() &&
		    !IsRsoPresident(user) &&
		    isSignatory &&
		    WorkflowCurrentStepMatchesReviewer(user))
		{
			canApprove = true;
		}

		_showFieldReviewControls = !isFullyApproved && isSignatory;
		_canInteractFieldReviewControls = canApprove;
		SubmitFieldReviewBtn.IsVisible = _canInteractFieldReviewControls;
		UpdateSubmitButtonState();
	}

	private string ResolveCurrentApprover()
	{
		if (_steps.Count > 0)
		{
			var current = _steps.FirstOrDefault(s => s.IsCurrentStep);
			if (current is not null && !string.IsNullOrWhiteSpace(current.RoleName))
			{
				return current.RoleName;
			}
		}

		if (_proposal is not null && !string.IsNullOrWhiteSpace(_proposal.CurrentStage))
		{
			return _proposal.CurrentStage;
		}

		return "—";
	}

	// ──────────────────────────────────────────────────────────────────────────
	// Summary card
	// ──────────────────────────────────────────────────────────────────────────

	private void BindSummaryCard()
	{
		if (_proposal is null)
		{
			return;
		}

		DocTypeLabel.Text = "Activity Proposal";
		SummaryOrgLabel.Text = _proposal.OrganizationName;
		SummarySubmittedByLabel.Text = _proposal.SubmittedBy;
		SummarySubmittedOnLabel.Text = _proposal.SubmittedDate == default
			? "—"
			: _proposal.SubmittedDate.ToString("MMM dd, yyyy");

		var stepIndex = FindCurrentStepIndex();
		SummaryCurrentStepLabel.Text = stepIndex >= 0 && stepIndex < _steps.Count
			? $"#{stepIndex + 1} — {_steps[stepIndex].RoleName}"
			: _proposal.CurrentStage;

		SummaryTitleLabel.Text = _proposal.Title;
		SummaryDatesLabel.Text = _proposal.ActivityDate == default
			? "—"
			: _proposal.ActivityDate.ToString("MMM dd, yyyy");
		SummaryVenueLabel.Text = string.IsNullOrWhiteSpace(_proposal.Venue) ? "—" : _proposal.Venue;
	}

	// ──────────────────────────────────────────────────────────────────────────
	// Fields
	// ──────────────────────────────────────────────────────────────────────────

	private static List<ProposalFieldReview> BuildFieldList(Proposal p, IReadOnlyList<ProposalAttachment> attachments)
	{
		var ext = p.ExtraData;

		// Activity dates: prefer real start/end from API; fall back to ActivityDate alone.
		// Try multiple Laravel naming variants for start/end date.
		var startDate = p.ActivityDate == default
			? ReadExtraDateAny(ext, "proposed_start_date", "date_of_activity", "start_date", "date_from", "activity_start_date", "activity_date_start")
			: p.ActivityDate;
		var endDate = ReadExtraDateAny(ext, "proposed_end_date", "end_date", "date_to", "activity_end_date", "activity_date_end");
		var dateOfActivity = startDate.HasValue ? startDate.Value.ToString("MMM dd, yyyy") : "—";
		var proposedDates = FormatDateRange(startDate, endDate,
			GetExtraStringAny(ext, "proposed_dates", "proposal_dates", "activity_dates"));

		// Activity time range: prefer the explicit start/end pair from the API.
		var startTime = GetExtraStringAny(ext, "proposed_start_time", "start_time", "time_from", "time_start");
		var endTime = GetExtraStringAny(ext, "proposed_end_time", "end_time", "time_to", "time_end");
		var proposedTime = FormatTimeRange(startTime, endTime,
			GetExtraStringAny(ext, "proposed_time", "activity_time"));

		// Academic term comes through as a nested object: { academic_year, semester }.
		var academicYearRaw = FirstNonEmpty(
			GetNestedExtraString(ext, "academic_term", "academic_year"),
			GetExtraStringAny(ext, "academic_year", "school_year", "ay"),
			"—");
		var semesterRaw = FirstNonEmpty(
			GetNestedExtraString(ext, "academic_term", "semester"),
			GetExtraString(ext, "semester"),
			string.Empty);
		var academicYear = string.IsNullOrWhiteSpace(semesterRaw) || academicYearRaw == "—"
			? academicYearRaw
			: $"{academicYearRaw} • {FormatSemester(semesterRaw)}";

		// "Department" maps cleanest to organization.college_school; school_code is a fallback.
		var department = FirstNonEmpty(
			GetNestedExtraString(ext, "organization", "college_school"),
			GetNestedExtraString(ext, "organization", "college"),
			GetNestedExtraString(ext, "organization", "school"),
			GetExtraStringAny(ext, "school_code", "department", "college", "college_school", "school", "college_name"),
			"—");

		var program = FirstNonEmpty(
			GetExtraStringAny(ext, "program", "course", "program_of_study", "program_name"),
			"—");
		var overallGoal = FirstNonEmpty(
			GetExtraStringAny(ext, "overall_goal", "goal", "objective_overall"),
			p.Description);
		var specificObjectives = FirstNonEmpty(
			GetExtraStringAny(ext, "specific_objectives", "objectives", "specific_objective", "specific_goals"),
			"—");
		var criteriaMechanics = FirstNonEmpty(
			GetExtraStringAny(ext, "criteria_mechanics", "mechanics", "criteria", "criteria_and_mechanics", "mechanics_criteria"),
			"—");
		var programFlow = FirstNonEmpty(
			GetExtraStringAny(ext, "program_flow", "activity_flow", "flow", "program_of_activities", "schedule_of_activities"),
			"—");
		var sourceOfFunding = FirstNonEmpty(
			GetExtraStringAny(ext, "source_of_funding", "funding_source", "budget_source", "source_of_funds", "fund_source"),
			"—");
		var targetSdg = FirstNonEmpty(
			GetExtraStringAny(ext, "target_sdg", "target_sdgs", "sdg", "sdgs", "sdg_target", "sustainable_development_goals"),
			"—");
		// Prefer explicit backend field for nature, fall back to inferred flow type.
		var explicitNature = GetExtraStringAny(ext, "nature_of_activity", "nature", "activity_nature");
		var natureOfActivity = !string.IsNullOrWhiteSpace(explicitNature)
			? explicitNature
			: (p.ApprovalFlowType == ApprovalFlowType.Academic ? "Co-curricular" : "Non-curricular");
		var typeOfActivity = FirstNonEmpty(
			GetExtraStringAny(ext, "activity_types", "activity_type", "type_of_activity", "activity_kind", "type", "category"),
			"—");
		var partnerEntities = FirstNonEmpty(
			GetExtraStringAny(ext, "partner_entities", "partners", "partner_organizations", "collaborators", "partner", "partner_entity"),
			"—");
		var proposalOption = FirstNonEmpty(
			GetExtraStringAny(ext,
				"proposal_option", "calendar_status", "is_in_calendar",
				"in_calendar", "is_calendar_event", "calendar_inclusion"),
			"—");

		var budgetRows = GetBudgetRows(ext, p.Budget);
		var totalFromRows = budgetRows.Sum(r => r.Price);
		var budgetTotal = totalFromRows > 0 ? totalFromRows : p.Budget;
		var requestLetterAttachment = FindAttachment(attachments, "request_letter", "upload_request_letter", "request-letter");
		var resumeAttachment = FindAttachment(attachments, "resume", "resume_of_speaker");
		var postSurveyAttachment = FindAttachment(attachments, "post_survey_form", "sample_post_survey_form", "post-survey");
		var organizationLogoAttachment = FindAttachment(attachments, "organization_logo", "org_logo", "logo");

		return
		[
			// ── Section 1: Submission overview ─────────────────────────────
			new() { StepKey = "step1", Label = "Proposal Option",       Value = proposalOption },
			new() { StepKey = "step1", Label = "RSO Name",              Value = string.IsNullOrWhiteSpace(p.OrganizationName) ? "—" : p.OrganizationName },
			new() { StepKey = "step1", Label = "Title of Activity",     Value = string.IsNullOrWhiteSpace(p.Title) ? "—" : p.Title },
			new() { StepKey = "step1", Label = "Partner Entities",      Value = partnerEntities },
			new() { StepKey = "step1", Label = "Nature of Activity",    Value = natureOfActivity },
			new() { StepKey = "step1", Label = "Type of Activity",      Value = typeOfActivity },
			new() { StepKey = "step1", Label = "Target SDG",            Value = targetSdg },
			new() { StepKey = "step1", Label = "Step 1 Proposed Budget",Value = p.Budget > 0 ? $"PHP {p.Budget:N2}" : "—" },
			new() { StepKey = "step1", Label = "Step 1 Budget Source",  Value = sourceOfFunding },
			new() { StepKey = "step1", Label = "Date of Activity",      Value = dateOfActivity },
			new() { StepKey = "step1", Label = "Venue",                 Value = string.IsNullOrWhiteSpace(p.Venue) ? "—" : p.Venue },
			new() { StepKey = "step1", Label = "Upload Request Letter", Value = BuildAttachmentValue(requestLetterAttachment), IsFile = true, Attachment = requestLetterAttachment },
			new() { StepKey = "step1", Label = "Resume of Speaker",     Value = BuildAttachmentValue(resumeAttachment), IsFile = true, Attachment = resumeAttachment },
			new() { StepKey = "step1", Label = "Sample Post-Survey Form", Value = BuildAttachmentValue(postSurveyAttachment), IsFile = true, Attachment = postSurveyAttachment },

			// ── Step 2: Proposal Submission ────────────────────────────────
			new() { StepKey = "step2", Label = "Organization Logo",     Value = BuildAttachmentValue(organizationLogoAttachment), IsFile = true, Attachment = organizationLogoAttachment },
			new() { StepKey = "step2", Label = "Organization",          Value = string.IsNullOrWhiteSpace(p.OrganizationName) ? "—" : p.OrganizationName },
			new() { StepKey = "step2", Label = "Academic Year",         Value = academicYear },
			new() { StepKey = "step2", Label = "Department",            Value = department },
			new() { StepKey = "step2", Label = "Program",               Value = program },
			new() { StepKey = "step2", Label = "Project / Activity Title", Value = string.IsNullOrWhiteSpace(p.Title) ? "—" : p.Title },
			new() { StepKey = "step2", Label = "Proposed Dates",        Value = proposedDates },
			new() { StepKey = "step2", Label = "Proposed Time",         Value = proposedTime },
			new() { StepKey = "step2", Label = "Venue",                 Value = string.IsNullOrWhiteSpace(p.Venue) ? "—" : p.Venue },
			new() { StepKey = "step2", Label = "Overall Goal",          Value = overallGoal },
			new() { StepKey = "step2", Label = "Specific Objectives",   Value = specificObjectives },
			new() { StepKey = "step2", Label = "Criteria / Mechanics",  Value = criteriaMechanics },
			new() { StepKey = "step2", Label = "Program Flow",          Value = programFlow },
			new() { StepKey = "step2", Label = "Proposed Budget (Total)", Value = budgetTotal > 0 ? $"PHP {budgetTotal:N2}" : "—" },
			new() { StepKey = "step2", Label = "Source of Funding",     Value = sourceOfFunding },
			new()
			{
				StepKey = "step2",
				Label = "Detailed Budget Table",
				Value = $"Rows: {budgetRows.Count} - Total: PHP {budgetTotal:N2}",
				BudgetRows = budgetRows
			},
		];
	}

	private static DateTime? ReadExtraDate(Dictionary<string, JsonElement>? ext, string key)
	{
		if (ext is null || !ext.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
		{
			return null;
		}

		var s = el.GetString();
		if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
			System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
			out var dt))
		{
			return dt;
		}
		return null;
	}

	private static DateTime? ReadExtraDateAny(Dictionary<string, JsonElement>? ext, params string[] keys)
	{
		if (ext is null) return null;
		for (var i = 0; i < keys.Length; i++)
		{
			var v = ReadExtraDate(ext, keys[i]);
			if (v.HasValue) return v;
		}
		for (var i = 0; i < keys.Length; i++)
		{
			if (TryGetDeepValue(ext, keys[i], out var deep) &&
			    deep.ValueKind == JsonValueKind.String &&
			    DateTime.TryParse(
				    deep.GetString(),
				    System.Globalization.CultureInfo.InvariantCulture,
				    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
				    out var dt))
			{
				return dt;
			}
		}
		return null;
	}

	private static string? GetExtraStringAny(Dictionary<string, JsonElement>? ext, params string[] keys)
	{
		if (ext is null) return null;
		for (var i = 0; i < keys.Length; i++)
		{
			var v = GetExtraString(ext, keys[i]);
			if (!string.IsNullOrWhiteSpace(v)) return v;
		}
		for (var i = 0; i < keys.Length; i++)
		{
			if (TryGetDeepValue(ext, keys[i], out var deep))
			{
				var deepString = ValueToString(deep);
				if (!string.IsNullOrWhiteSpace(deepString))
				{
					return deepString;
				}
			}
		}
		return null;
	}

	private static string? GetNestedExtraString(Dictionary<string, JsonElement>? ext, string parent, string child)
	{
		if (ext is null || !ext.TryGetValue(parent, out var p) || p.ValueKind != JsonValueKind.Object)
		{
			if (ext is null)
			{
				return null;
			}

			if (TryGetDeepNestedValue(ext, parent, child, out var deepNested))
			{
				return ValueToString(deepNested);
			}

			return null;
		}

		if (!p.TryGetProperty(child, out var v))
		{
			return null;
		}

		return v.ValueKind switch
		{
			JsonValueKind.String => v.GetString(),
			JsonValueKind.Number => v.ToString(),
			_ => null
		};
	}

	private static string? ValueToString(JsonElement value)
	{
		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString(),
			JsonValueKind.Number => value.ToString(),
			JsonValueKind.True => "Yes",
			JsonValueKind.False => "No",
			JsonValueKind.Array => JoinArrayElements(value),
			_ => null
		};
	}

	private static bool TryGetDeepValue(Dictionary<string, JsonElement> ext, string key, out JsonElement value)
	{
		if (ext.TryGetValue(key, out value))
		{
			return true;
		}

		foreach (var kv in ext)
		{
			if (TryGetDeepValueFromNode(kv.Value, key, out value))
			{
				return true;
			}
		}

		value = default;
		return false;
	}

	private static bool TryGetDeepValueFromNode(JsonElement node, string key, out JsonElement value)
	{
		switch (node.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var child in node.EnumerateObject())
				{
					if (string.Equals(child.Name, key, StringComparison.OrdinalIgnoreCase))
					{
						value = child.Value;
						return true;
					}
				}
				foreach (var child in node.EnumerateObject())
				{
					if (TryGetDeepValueFromNode(child.Value, key, out value))
					{
						return true;
					}
				}
				break;
			case JsonValueKind.Array:
				foreach (var item in node.EnumerateArray())
				{
					if (TryGetDeepValueFromNode(item, key, out value))
					{
						return true;
					}
				}
				break;
		}

		value = default;
		return false;
	}

	private static bool TryGetDeepNestedValue(Dictionary<string, JsonElement> ext, string parent, string child, out JsonElement value)
	{
		foreach (var kv in ext)
		{
			if (TryGetDeepNestedValueFromNode(kv.Value, parent, child, out value))
			{
				return true;
			}
		}

		value = default;
		return false;
	}

	private static bool TryGetDeepNestedValueFromNode(JsonElement node, string parent, string child, out JsonElement value)
	{
		switch (node.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var prop in node.EnumerateObject())
				{
					if (string.Equals(prop.Name, parent, StringComparison.OrdinalIgnoreCase) &&
					    prop.Value.ValueKind == JsonValueKind.Object &&
					    TryGetPropertyCaseInsensitive(prop.Value, child, out value))
					{
						return true;
					}
				}
				foreach (var prop in node.EnumerateObject())
				{
					if (TryGetDeepNestedValueFromNode(prop.Value, parent, child, out value))
					{
						return true;
					}
				}
				break;
			case JsonValueKind.Array:
				foreach (var item in node.EnumerateArray())
				{
					if (TryGetDeepNestedValueFromNode(item, parent, child, out value))
					{
						return true;
					}
				}
				break;
		}

		value = default;
		return false;
	}

	private static bool TryGetPropertyCaseInsensitive(JsonElement obj, string propertyName, out JsonElement value)
	{
		if (obj.ValueKind == JsonValueKind.Object)
		{
			foreach (var prop in obj.EnumerateObject())
			{
				if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
				{
					value = prop.Value;
					return true;
				}
			}
		}

		value = default;
		return false;
	}

	private static string FormatDateRange(DateTime? start, DateTime? end, params string?[] fallbacks)
	{
		if (start.HasValue && end.HasValue && end.Value.Date != start.Value.Date)
		{
			return $"{start.Value:MMM dd, yyyy} – {end.Value:MMM dd, yyyy}";
		}
		if (start.HasValue)
		{
			return start.Value.ToString("MMM dd, yyyy");
		}
		if (end.HasValue)
		{
			return end.Value.ToString("MMM dd, yyyy");
		}
		return FirstNonEmpty(fallbacks);
	}

	private static string FormatTimeRange(string? start, string? end, params string?[] fallbacks)
	{
		var hasStart = !string.IsNullOrWhiteSpace(start);
		var hasEnd = !string.IsNullOrWhiteSpace(end);
		if (hasStart && hasEnd)
		{
			return $"{NormalizeTime(start!)} – {NormalizeTime(end!)}";
		}
		if (hasStart)
		{
			return NormalizeTime(start!);
		}
		if (hasEnd)
		{
			return NormalizeTime(end!);
		}
		return FirstNonEmpty(fallbacks);
	}

	private static string NormalizeTime(string raw)
	{
		// Try a few common formats coming from Laravel: "HH:mm:ss", "HH:mm", "h:mm tt".
		if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t))
		{
			return t.ToString("h:mm tt");
		}
		return raw.Trim();
	}

	private static string FormatSemester(string raw)
	{
		var trimmed = raw.Trim();
		return trimmed.ToLowerInvariant() switch
		{
			"first" => "1st Semester",
			"1st" => "1st Semester",
			"second" => "2nd Semester",
			"2nd" => "2nd Semester",
			"summer" => "Summer",
			_ => trimmed
		};
	}

	private static ProposalAttachment? FindAttachment(IReadOnlyList<ProposalAttachment> attachments, params string[] typeHints)
	{
		if (attachments.Count == 0)
		{
			return null;
		}

		for (var i = 0; i < attachments.Count; i++)
		{
			var type = attachments[i].FileType ?? string.Empty;
			for (var j = 0; j < typeHints.Length; j++)
			{
				if (type.Contains(typeHints[j], StringComparison.OrdinalIgnoreCase))
				{
					return attachments[i];
				}
			}
		}

		return null;
	}

	private static string BuildAttachmentValue(ProposalAttachment? attachment)
	{
		if (attachment is null)
		{
			return "No uploaded file found.";
		}

		return attachment.DisplayName;
	}

	private static string FirstNonEmpty(params string?[] values)
	{
		for (var i = 0; i < values.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(values[i]))
			{
				return values[i]!.Trim();
			}
		}
		return "—";
	}

	private static string? GetExtraString(Dictionary<string, JsonElement>? ext, string key)
	{
		if (ext is null || !ext.TryGetValue(key, out var el))
		{
			return null;
		}

		return el.ValueKind switch
		{
			JsonValueKind.String => el.GetString(),
			JsonValueKind.Number => el.ToString(),
			JsonValueKind.True => "Yes",
			JsonValueKind.False => "No",
			JsonValueKind.Array => JoinArrayElements(el),
			_ => null
		};
	}

	private static string? JoinArrayElements(JsonElement arr)
	{
		var items = new List<string>();
		foreach (var entry in arr.EnumerateArray())
		{
			switch (entry.ValueKind)
			{
				case JsonValueKind.String:
					var s = entry.GetString();
					if (!string.IsNullOrWhiteSpace(s)) items.Add(s.Trim());
					break;
				case JsonValueKind.Number:
					items.Add(entry.ToString());
					break;
				case JsonValueKind.Object:
					// Common Laravel pattern: list of related objects with a `name`/`title` property.
					var labelGuess =
						(entry.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null) ??
						(entry.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null) ??
						(entry.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null);
					if (!string.IsNullOrWhiteSpace(labelGuess)) items.Add(labelGuess!.Trim());
					break;
			}
		}
		return items.Count == 0 ? null : string.Join(", ", items);
	}

	private static List<BudgetTableRow> GetBudgetRows(Dictionary<string, JsonElement>? ext, decimal fallbackBudget)
	{
		if (ext is not null)
		{
			var keys = new[]
			{
				"detailed_budget_table", "budget_rows", "budget_table", "budget_items",
				"materials", "proposed_budget_items", "budget_breakdown", "budget_items_payload", "items",
				"budget", "budgets"
			};
			for (var i = 0; i < keys.Length; i++)
			{
				if (TryParseBudgetRows(ext, keys[i], out var rows) && rows.Count > 0)
				{
					return rows;
				}
			}
		}

		// Empty list when there are no real rows — the field card simply shows the
		// summary line without a fabricated row.
		if (fallbackBudget <= 0)
		{
			return [];
		}

		return
		[
			new BudgetTableRow
			{
				Material = "pc",
				Quantity = 10.00m,
				UnitPrice = fallbackBudget / 10.00m
			}
		];
	}

	private static bool TryParseBudgetRows(Dictionary<string, JsonElement> ext, string key, out List<BudgetTableRow> rows)
	{
		rows = [];
		if (!ext.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Array)
		{
			return false;
		}

		foreach (var item in el.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			var material = ReadString(item, "material", "item", "name", "description");
			var qty = ReadDecimal(item, "quantity", "qty");
			var unitPrice = ReadDecimal(item, "unit_price", "unitPrice", "price_per_unit");
			var price = ReadDecimal(item, "price", "total", "amount");

			if (qty <= 0 && unitPrice > 0 && price > 0)
			{
				qty = price / unitPrice;
			}

			if (unitPrice <= 0 && qty > 0 && price > 0)
			{
				unitPrice = price / qty;
			}

			rows.Add(new BudgetTableRow
			{
				Material = string.IsNullOrWhiteSpace(material) ? "—" : material,
				Quantity = qty > 0 ? qty : 0,
				UnitPrice = unitPrice > 0 ? unitPrice : 0
			});
		}

		return rows.Count > 0;
	}

	private static string ReadString(JsonElement obj, params string[] keys)
	{
		for (var i = 0; i < keys.Length; i++)
		{
			if (obj.TryGetProperty(keys[i], out var p) && p.ValueKind == JsonValueKind.String)
			{
				var s = p.GetString();
				if (!string.IsNullOrWhiteSpace(s))
				{
					return s.Trim();
				}
			}
		}
		return string.Empty;
	}

	private static decimal ReadDecimal(JsonElement obj, params string[] keys)
	{
		for (var i = 0; i < keys.Length; i++)
		{
			if (!obj.TryGetProperty(keys[i], out var p))
			{
				continue;
			}

			if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d))
			{
				return d;
			}

			if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), out var ds))
			{
				return ds;
			}
		}
		return 0m;
	}

	private void BuildFieldCards()
	{
		Step1FieldsStack.Children.Clear();
		Step2FieldsStack.Children.Clear();

		for (var i = 0; i < _fields.Count; i++)
		{
			var card = CreateFieldCard(i);
			if (_fields[i].StepKey == "step1")
			{
				Step1FieldsStack.Children.Add(card);
			}
			else
			{
				Step2FieldsStack.Children.Add(card);
			}
		}
	}

	private View CreateFieldCard(int index)
	{
		var field = _fields[index];

		// --- Border around the whole card ---
		var cardBorder = new Border
		{
			BackgroundColor = Colors.White,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
			StrokeThickness = 1,
			Padding = new Thickness(12)
		};
		ApplyFieldCardBorderColor(cardBorder, field.State);

		var cardContent = new VerticalStackLayout { Spacing = 8 };

		// --- Top row: label+value | Passed | Revision buttons ---
		var topRow = new Grid
		{
			ColumnDefinitions = _showFieldReviewControls
				? new ColumnDefinitionCollection
				{
					new ColumnDefinition(GridLength.Star),
					new ColumnDefinition(GridLength.Auto),
					new ColumnDefinition(GridLength.Auto)
				}
				: new ColumnDefinitionCollection
				{
					new ColumnDefinition(GridLength.Star)
				},
			ColumnSpacing = 8
		};

		var labelStack = new VerticalStackLayout { Spacing = 2 };
		var fieldLabel = new Label
		{
			Text = field.Label.ToUpperInvariant(),
			FontSize = 9,
			FontAttributes = FontAttributes.Bold,
			TextColor = PrimaryColor,
			Opacity = 0.6
		};
		var fieldValue = new Label
		{
			Text = field.Value,
			FontSize = 13,
			TextColor = Color.FromArgb("#1A2340"),
			LineBreakMode = LineBreakMode.WordWrap
		};
		labelStack.Children.Add(fieldLabel);
		labelStack.Children.Add(fieldValue);

		if (field.BudgetRows.Count > 0)
		{
			var tableHeader = new Grid
			{
				ColumnDefinitions =
				{
					new ColumnDefinition(GridLength.Star),
					new ColumnDefinition(GridLength.Star),
					new ColumnDefinition(GridLength.Star),
					new ColumnDefinition(GridLength.Star)
				},
				ColumnSpacing = 8,
				Margin = new Thickness(0, 6, 0, 0)
			};
			var materialHeader = new Label { Text = "MATERIAL", FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#6A7690") };
			var quantityHeader = new Label { Text = "QUANTITY", FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#6A7690") };
			var unitPriceHeader = new Label { Text = "UNIT PRICE", FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#6A7690") };
			var priceHeader = new Label { Text = "PRICE", FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#6A7690") };
			Grid.SetColumn(materialHeader, 0);
			Grid.SetColumn(quantityHeader, 1);
			Grid.SetColumn(unitPriceHeader, 2);
			Grid.SetColumn(priceHeader, 3);
			tableHeader.Children.Add(materialHeader);
			tableHeader.Children.Add(quantityHeader);
			tableHeader.Children.Add(unitPriceHeader);
			tableHeader.Children.Add(priceHeader);
			labelStack.Children.Add(tableHeader);

			foreach (var row in field.BudgetRows)
			{
				var dataRow = new Grid
				{
					ColumnDefinitions =
					{
						new ColumnDefinition(GridLength.Star),
						new ColumnDefinition(GridLength.Star),
						new ColumnDefinition(GridLength.Star),
						new ColumnDefinition(GridLength.Star)
					},
					ColumnSpacing = 8,
					Margin = new Thickness(0, 2, 0, 0)
				};

				var materialValue = new Label { Text = row.Material, FontSize = 12, TextColor = Color.FromArgb("#1A2340") };
				var quantityValue = new Label { Text = row.Quantity.ToString("N2"), FontSize = 12, TextColor = Color.FromArgb("#1A2340") };
				var unitPriceValue = new Label { Text = row.UnitPrice.ToString("N2"), FontSize = 12, TextColor = Color.FromArgb("#1A2340") };
				var priceValue = new Label { Text = row.Price.ToString("N2"), FontSize = 12, TextColor = Color.FromArgb("#1A2340") };
				Grid.SetColumn(materialValue, 0);
				Grid.SetColumn(quantityValue, 1);
				Grid.SetColumn(unitPriceValue, 2);
				Grid.SetColumn(priceValue, 3);
				dataRow.Children.Add(materialValue);
				dataRow.Children.Add(quantityValue);
				dataRow.Children.Add(unitPriceValue);
				dataRow.Children.Add(priceValue);
				labelStack.Children.Add(dataRow);
			}
		}

		if (field.IsFile)
		{
			var fileMeta = new Label
			{
				Text = field.Attachment is null ? "No uploaded file found." : field.Value,
				FontSize = 11,
				TextColor = Color.FromArgb("#5A6A8A"),
				LineBreakMode = LineBreakMode.WordWrap
			};
			labelStack.Children.Add(fileMeta);

			if (field.Attachment is not null)
			{
				var openLink = new Label
				{
					Text = "Open / Download file ↗",
					FontSize = 11,
					TextColor = PrimaryColor,
					TextDecorations = TextDecorations.Underline
				};
				var tap = new TapGestureRecognizer();
				tap.Tapped += async (_, _) => await OpenAttachmentAsync(field.Attachment, asDownload: false);
				openLink.GestureRecognizers.Add(tap);
				labelStack.Children.Add(openLink);
			}
		}

		Grid.SetColumn(labelStack, 0);

		topRow.Children.Add(labelStack);
		if (_showFieldReviewControls && field.IsReviewable)
		{
			// Passed button
			var passedBtn = CreateReviewButton(
				"Passed",
				index,
				isPassed: true,
				field.State == FieldReviewState.Passed,
				isEnabled: _canInteractFieldReviewControls);
			Grid.SetColumn(passedBtn, 1);
			topRow.Children.Add(passedBtn);

			// Revision button
			var revisionBtn = CreateReviewButton(
				"Revision",
				index,
				isPassed: false,
				field.State == FieldReviewState.Revision,
				isEnabled: _canInteractFieldReviewControls);
			Grid.SetColumn(revisionBtn, 2);
			topRow.Children.Add(revisionBtn);
		}
		cardContent.Children.Add(topRow);

		// --- Revision note input (visible only when Revision selected) ---
		var revisionNoteSection = new VerticalStackLayout
		{
			Spacing = 4,
			IsVisible = _canInteractFieldReviewControls && field.IsReviewable && field.RevisionInputVisible
		};

		var noteEntry = new Editor
		{
			Placeholder = "Add revision note for this field...",
			MinimumHeightRequest = 70,
			BackgroundColor = Colors.White,
			TextColor = Color.FromArgb("#1A2340"),
			Text = field.RevisionNote,
			FontSize = 12
		};
		noteEntry.TextChanged += (_, e) =>
		{
			_fields[index].RevisionNote = e.NewTextValue ?? string.Empty;
			UpdateReviewSummary();
			UpdateSubmitButtonState();
			QueueFieldReviewAutoSave();
		};

		var hintLabel = new Label
		{
			Text = "Required when this field needs revision.",
			FontSize = 11,
			TextColor = Color.FromArgb("#5A6A8A")
		};

		var noteValidationLabel = new Label
		{
			Text = "Revision note is required.",
			FontSize = 11,
			TextColor = RevisionText,
			IsVisible = false
		};

		var noteBorder = new Border
		{
			BackgroundColor = Colors.White,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
			StrokeThickness = 1.5,
			Stroke = RevisionBorder,
			Padding = new Thickness(10),
			Content = noteEntry
		};

		revisionNoteSection.Children.Add(noteBorder);
		revisionNoteSection.Children.Add(hintLabel);
		revisionNoteSection.Children.Add(noteValidationLabel);

		cardContent.Children.Add(revisionNoteSection);
		cardBorder.Content = cardContent;
		return cardBorder;
	}

	private Border CreateReviewButton(string text, int fieldIndex, bool isPassed, bool isActive, bool isEnabled)
	{
		var activeColor = isPassed ? PassedBorder : RevisionBorder;
		var activeText = isPassed ? PassedText : RevisionText;
		var activeBg = isPassed ? PassedBg : RevisionBg;

		var label = new Label
		{
			Text = text,
			FontSize = 11,
			FontAttributes = FontAttributes.Bold,
			TextColor = isActive ? activeText : PendingText,
			HorizontalTextAlignment = TextAlignment.Center
		};

		var btn = new Border
		{
			BackgroundColor = isActive ? activeBg : Colors.White,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
			StrokeThickness = 1.5,
			Stroke = isActive ? activeColor : PendingBorder,
			Padding = new Thickness(10, 5),
			Content = label,
			MinimumWidthRequest = 64,
			Opacity = isEnabled ? 1.0 : 0.55
		};

		if (isEnabled)
		{
			var tapGesture = new TapGestureRecognizer();
			tapGesture.Tapped += (_, _) => OnFieldReviewButtonTapped(fieldIndex, isPassed);
			btn.GestureRecognizers.Add(tapGesture);
		}

		return btn;
	}

	private void OnFieldReviewButtonTapped(int index, bool isPassed)
	{
		if (!_canInteractFieldReviewControls)
		{
			return;
		}

		var field = _fields[index];

		if (isPassed)
		{
			field.State = field.State == FieldReviewState.Passed
				? FieldReviewState.Pending
				: FieldReviewState.Passed;
			field.RevisionInputVisible = false;
		}
		else
		{
			field.State = field.State == FieldReviewState.Revision
				? FieldReviewState.Pending
				: FieldReviewState.Revision;
			field.RevisionInputVisible = field.State == FieldReviewState.Revision;
		}

		BuildFieldCards();
		UpdateProgressBar();
		UpdateReviewSummary();
		UpdateStepBadges();
		RefreshComputedStatusBadge();
		UpdateSubmitButtonState();
		QueueFieldReviewAutoSave();
	}

	private void UpdateSubmitButtonState()
	{
		if (!SubmitFieldReviewBtn.IsVisible)
		{
			SubmitFieldReviewBtn.IsEnabled = false;
			SubmitFieldReviewHintLabel.IsVisible = false;
			return;
		}

		if (!_canInteractFieldReviewControls)
		{
			SubmitFieldReviewBtn.IsEnabled = false;
			SubmitFieldReviewBtn.Opacity = 0.55;
			SubmitFieldReviewHintLabel.Text = "It's not your turn to approve this proposal yet.";
			SubmitFieldReviewHintLabel.IsVisible = true;
			return;
		}

		var hasAnyFields = _fields.Count > 0;
		var undecidedCount = _fields.Count(f => f.State == FieldReviewState.Pending);
		var revisionMissingNoteCount = _fields.Count(f =>
			f.State == FieldReviewState.Revision &&
			string.IsNullOrWhiteSpace(f.RevisionNote));
		var canSubmit = hasAnyFields && undecidedCount == 0 && revisionMissingNoteCount == 0;

		SubmitFieldReviewBtn.IsEnabled = canSubmit;
		SubmitFieldReviewBtn.Opacity = canSubmit ? 1.0 : 0.55;

		if (canSubmit)
		{
			SubmitFieldReviewHintLabel.IsVisible = false;
			return;
		}

		SubmitFieldReviewHintLabel.IsVisible = true;
		SubmitFieldReviewHintLabel.Text = revisionMissingNoteCount > 0
			? "Please add revision notes for all fields marked as Revision before submitting."
			: "Please complete all field review decisions before submitting.";
	}

	private static bool IsAnySignatoryForProposal(User? user, Proposal? proposal)
	{
		if (user is null || proposal is null)
		{
			return false;
		}

		var hints = ApprovalRules.GetReviewerRoleHints(user);
		var stages = ProposalWorkflowService.GetStages(proposal.ApprovalFlowType);
		return hints.Any(h => stages.Any(s => ProposalWorkflowService.IsEquivalentRole(s, h)));
	}

	private static bool DoesUserMatchCurrentStage(User? user, Proposal proposal)
	{
		if (user is null)
		{
			return false;
		}

		var hints = ApprovalRules.GetReviewerRoleHints(user);
		return hints.Any(h => ProposalWorkflowService.IsEquivalentRole(proposal.CurrentStage, h));
	}

	private static bool IsRsoPresident(User? user) =>
		user is not null &&
		(string.Equals(user.Role, "RSO President", StringComparison.OrdinalIgnoreCase) ||
		 string.Equals(user.Role, "Organization Officer", StringComparison.OrdinalIgnoreCase) ||
		 string.Equals(user.RoleKey, "rso_president", StringComparison.OrdinalIgnoreCase) ||
		 string.Equals(user.RoleKey, "org_officer", StringComparison.OrdinalIgnoreCase));

	private static bool IsActionableWorkflowStatus(string? status)
	{
		var normalized = Proposal.NormalizeStatus(status);
		return string.Equals(normalized, "Pending", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(normalized, "Under Review", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(normalized, "Submitted", StringComparison.OrdinalIgnoreCase);
	}

	private bool IsAllStepsPassed() => StepAllPassed("step1") && StepAllPassed("step2");

	/// <summary>
	/// UI-only computed status: when all fields are passed in both steps,
	/// show Approved immediately (even before submit tap).
	/// </summary>
	private void RefreshComputedStatusBadge()
	{
		if (_proposal is null)
		{
			return;
		}

		var computed = IsAllStepsPassed() ? "Approved" : _proposal.Status;
		StatusLabel.Text = computed;
		ApplyStatusBadge(computed);
	}

	private static void ApplyFieldCardBorderColor(Border card, FieldReviewState state)
	{
		card.Stroke = state switch
		{
			FieldReviewState.Passed => PassedBorder,
			FieldReviewState.Revision => RevisionBorder,
			_ => PendingBorder
		};
	}

	// ──────────────────────────────────────────────────────────────────────────
	// Progress bar + badges
	// ──────────────────────────────────────────────────────────────────────────

	// Steps are verified/revision/pending at the STEP level (max 2 steps total).
	private void UpdateProgressBar()
	{
		var verified = CountStepState(FieldReviewState.Passed);
		var revision = CountStepRevision();
		var pending = 2 - verified - revision;

		VerifiedCountLabel.Text = $"Verified: {verified}";
		RevisionCountLabel.Text = $"Revision: {revision}";
		PendingCountLabel.Text = $"Pending: {pending}";
	}

	private bool StepAllPassed(string key) =>
		_fields.Where(f => f.StepKey == key).All(f => f.State == FieldReviewState.Passed);

	private bool StepHasRevision(string key) =>
		!StepAllPassed(key) && _fields.Where(f => f.StepKey == key).Any(f => f.State == FieldReviewState.Revision);

	private int CountStepState(FieldReviewState target)
	{
		var count = 0;
		if (target == FieldReviewState.Passed)
		{
			if (StepAllPassed("step1")) count++;
			if (StepAllPassed("step2")) count++;
		}
		return count;
	}

	private int CountStepRevision()
	{
		var count = 0;
		if (StepHasRevision("step1")) count++;
		if (StepHasRevision("step2")) count++;
		return count;
	}

	private void UpdateStepBadges()
	{
		UpdateStepBadge("step1", Step1Badge, Step1BadgeLabel);
		UpdateStepBadge("step2", Step2Badge, Step2BadgeLabel);
	}

	private void UpdateStepBadge(string stepKey, Border badge, Label badgeLabel)
	{
		var stepFields = _fields.Where(f => f.StepKey == stepKey).ToList();
		if (stepFields.Count == 0)
		{
			return;
		}

		if (stepFields.All(f => f.State == FieldReviewState.Passed))
		{
			badge.BackgroundColor = PassedBg;
			badge.Stroke = PassedBorder;
			badgeLabel.Text = "Verified";
			badgeLabel.TextColor = PassedText;
		}
		else if (stepFields.Any(f => f.State == FieldReviewState.Revision))
		{
			badge.BackgroundColor = RevisionBg;
			badge.Stroke = RevisionBorder;
			badgeLabel.Text = "Needs Revision";
			badgeLabel.TextColor = RevisionText;
		}
		else
		{
			badge.BackgroundColor = Color.FromArgb("#F4F6FA");
			badge.Stroke = PendingBorder;
			badgeLabel.Text = "Pending";
			badgeLabel.TextColor = PendingText;
		}
	}

	// ──────────────────────────────────────────────────────────────────────────
	// Review summary
	// ──────────────────────────────────────────────────────────────────────────

	private void UpdateReviewSummary()
	{
		var verifiedSteps = CountStepState(FieldReviewState.Passed);
		var revisionSteps = CountStepRevision();
		var pendingSteps = 2 - verifiedSteps - revisionSteps;

		if (pendingSteps > 0)
		{
			ReviewSummaryCard.BackgroundColor = Color.FromArgb("#F4F6FA");
			ReviewSummaryCard.Stroke = PendingBorder;
			ReviewSummaryLabel.TextColor = Color.FromArgb("#3A4A6A");
			ReviewSummaryLabel.Text = $"{verifiedSteps}/2 steps verified. Review the remaining step(s) to continue.";
		}
		else if (revisionSteps > 0)
		{
			ReviewSummaryCard.BackgroundColor = RevisionBg;
			ReviewSummaryCard.Stroke = RevisionBorder;
			ReviewSummaryLabel.TextColor = Color.FromArgb("#4A2000");

			var notes = _fields.Where(f => f.State == FieldReviewState.Revision)
				.Select(f => $"• {f.Label}: {(string.IsNullOrWhiteSpace(f.RevisionNote) ? "(note needed)" : f.RevisionNote)}")
				.ToList();
			ReviewSummaryLabel.Text = $"Some fields need revision. Add revision notes and submit.\n\n{string.Join('\n', notes)}";
		}
		else
		{
			ReviewSummaryCard.BackgroundColor = Color.FromArgb("#E8F5EF");
			ReviewSummaryCard.Stroke = Color.FromArgb("#3CB371");
			ReviewSummaryLabel.TextColor = Color.FromArgb("#1A4030");
			ReviewSummaryLabel.Text = "Every section is fully reviewed and verified.\n\nAll sections are verified. No revision notes required. This submission is ready for finalization.";
		}
	}

	// ──────────────────────────────────────────────────────────────────────────
	// Approval workflow (horizontal track)
	// ──────────────────────────────────────────────────────────────────────────

	private void BuildWorkflowTrack()
	{
		WorkflowTrackStack.Children.Clear();

		// Build the display list: "Submitted" (always completed) + the signatory stages.
		var signatoryNames = _steps.Count > 0
			? _steps.Select(s => s.RoleName).ToArray()
			: ProposalWorkflowService.GetStages(_proposal?.ApprovalFlowType ?? ApprovalFlowType.Academic).ToArray();

		// stageNames[0] = "Submitted" (virtual, always completed).
		// stageNames[1..] = signatory stages mapped from _steps[0..].
		// _selectedStepIndex is 0-based into _steps; add 1 to get display index.
		var stageNames = new[] { "Submitted" }.Concat(signatoryNames).ToArray();
		var currentStepIndex = FindCurrentStepIndex();
		var displayCurrentIndex = currentStepIndex + 1; // +1 because Submitted is at index 0

		// Keep the selected stage pinned to the currently assigned signatory.
		// This prevents opening other stages as editable/interactive from the track.
		_selectedStepIndex = currentStepIndex;
		var displaySelectedIndex = displayCurrentIndex;

		for (var i = 0; i < stageNames.Length; i++)
		{
			var stage = stageNames[i];
			// "Submitted" is display index 0 — always completed.
			// Signatory steps start at display index 1, mapped to _steps[i-1].
			var stepIdx = i - 1;
			var step = (stepIdx >= 0 && stepIdx < _steps.Count) ? _steps[stepIdx] : null;
			var isCurrent = i == displaySelectedIndex;
			var isCompleted = i == 0 || step?.Status == "Completed";
			var isInteractive = stepIdx >= 0 && i == displayCurrentIndex;
			var isCaptured = i; // capture for lambda

			// Node circle
			var circleContent = new Label
			{
				Text = isCompleted ? "✓" : "·",
				HorizontalOptions = LayoutOptions.Center,
				VerticalOptions = LayoutOptions.Center,
				HorizontalTextAlignment = TextAlignment.Center,
				VerticalTextAlignment = TextAlignment.Center,
				TextColor = isCompleted ? Colors.White : isCurrent ? PrimaryColor : Color.FromArgb("#C0C8D8"),
				FontSize = isCompleted ? 13 : 10,
				FontAttributes = FontAttributes.Bold
			};

			var circleBorder = new Border
			{
				WidthRequest = 34,
				HeightRequest = 34,
				StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
				StrokeThickness = isCurrent ? 2.5 : 1.5,
				BackgroundColor = isCompleted ? Color.FromArgb("#3CB371") : Colors.White,
				Stroke = isCompleted ? Color.FromArgb("#2AA060") : isCurrent ? PrimaryColor : Color.FromArgb("#C0C8D8"),
				HorizontalOptions = LayoutOptions.Center,
				Content = circleContent,
				Opacity = isInteractive ? 1 : 0.9
			};

			if (isInteractive)
			{
				var tap = new TapGestureRecognizer();
				tap.Tapped += (_, _) =>
				{
					// Map display index back to _steps index (subtract 1 for Submitted node).
					_selectedStepIndex = Math.Max(0, isCaptured - 1);
					BuildWorkflowTrack();
					BindSelectedStageCard();
				};
				circleBorder.GestureRecognizers.Add(tap);
			}

			var stageLabel = new Label
			{
				Text = stage,
				FontSize = 9,
				TextColor = isCompleted ? Color.FromArgb("#2AA060") : isCurrent ? PrimaryColor : Color.FromArgb("#8090A8"),
				FontAttributes = isCurrent ? FontAttributes.Bold : FontAttributes.None,
				HorizontalTextAlignment = TextAlignment.Center,
				MaximumWidthRequest = 60,
				LineBreakMode = LineBreakMode.WordWrap,
				Opacity = isInteractive || isCurrent || isCompleted ? 1 : 0.75
			};

			var nodeStack = new VerticalStackLayout
			{
				Spacing = 4,
				HorizontalOptions = LayoutOptions.Center,
				MinimumWidthRequest = 62
			};
			nodeStack.Children.Add(circleBorder);
			nodeStack.Children.Add(stageLabel);

			if (isInteractive && circleBorder.GestureRecognizers.Count > 0)
			{
				nodeStack.GestureRecognizers.Add(circleBorder.GestureRecognizers[0]);
			}

			WorkflowTrackStack.Children.Add(nodeStack);

			// Connector line (except after last)
			if (i < stageNames.Length - 1)
			{
				var line = new BoxView
				{
					HeightRequest = 2,
					WidthRequest = 18,
					VerticalOptions = LayoutOptions.Start,
					Margin = new Thickness(0, 16, 0, 0),
					Color = isCompleted ? Color.FromArgb("#3CB371") : Color.FromArgb("#D0D8E8")
				};
				WorkflowTrackStack.Children.Add(line);
			}
		}
	}

	private void BindSelectedStageCard()
	{
		// _selectedStepIndex == -1 means the "Submitted" virtual node is selected.
		if (_selectedStepIndex < 0)
		{
			SelectedStageLabel.Text = "Submitted";
			SelectedStageStatusLabel.Text = "SUBMITTED";
			SelectedStageReviewerLabel.Text = _proposal?.SubmittedBy ?? "—";
			SelectedStageActedAtLabel.Text = _proposal?.SubmittedDate == default
				? "—"
				: _proposal!.SubmittedDate.ToString("MMM dd, yyyy");
			return;
		}

		if (_steps.Count == 0 || _selectedStepIndex >= _steps.Count)
		{
			SelectedStageLabel.Text = "—";
			SelectedStageStatusLabel.Text = "—";
			SelectedStageReviewerLabel.Text = "—";
			SelectedStageActedAtLabel.Text = "—";
			return;
		}

		var step = _steps[_selectedStepIndex];
		SelectedStageLabel.Text = step.RoleName;
		SelectedStageStatusLabel.Text = step.Status?.ToUpperInvariant() ?? "—";
		SelectedStageReviewerLabel.Text = step.ReviewedBy ?? "—";
		SelectedStageActedAtLabel.Text = step.ActedAt.HasValue
			? step.ActedAt.Value.ToString("MMM dd, yyyy")
			: "—";
	}

	// ──────────────────────────────────────────────────────────────────────────
	// Workflow logs
	// ──────────────────────────────────────────────────────────────────────────

	private void BuildWorkflowLogs()
	{
		WorkflowLogsStack.Children.Clear();

		var localDecisions = _fields
			.Where(f => f.State != FieldReviewState.Pending)
			.Take(8)
			.Select(f =>
			{
				var action = f.State == FieldReviewState.Passed ? "PASSED" : "REVISION";
				var note = f.State == FieldReviewState.Revision
					? (string.IsNullOrWhiteSpace(f.RevisionNote) ? " (note pending)" : $" · {f.RevisionNote.Trim()}")
					: string.Empty;
				return $"FIELD REVIEW: {action} · {f.Label}{note}";
			})
			.ToList();

		if (localDecisions.Count > 0)
		{
			foreach (var local in localDecisions)
			{
				WorkflowLogsStack.Children.Add(new Label
				{
					Text = local,
					FontSize = 11,
					TextColor = Color.FromArgb("#5A6A8A"),
					LineBreakMode = LineBreakMode.WordWrap
				});
			}
		}

		var logs = _steps
			.Where(s => s.Status == "Completed" || s.ActedAt.HasValue)
			.OrderByDescending(s => s.ActedAt)
			.Take(5)
			.ToList();

		var historyLogs = _historyEntries
			.Where(h => h.Timestamp != default)
			.OrderByDescending(h => h.Timestamp)
			.Take(8)
			.ToList();

		if (historyLogs.Count > 0)
		{
			foreach (var entry in historyLogs)
			{
				var line = $"{entry.DisplayTitle.ToUpperInvariant()}: {entry.DisplayActor}";
				if (!string.IsNullOrWhiteSpace(entry.DisplayActorRole))
				{
					line += $" · {entry.DisplayActorRole}";
				}
				if (!string.IsNullOrWhiteSpace(entry.DisplayStatusAfterAction))
				{
					line += $" · {entry.DisplayStatusAfterAction}";
				}
				if (!string.IsNullOrWhiteSpace(entry.DisplayRemark))
				{
					line += $" · {entry.DisplayRemark}";
				}
				line += $" · {entry.Timestamp:MMM dd, yyyy}";

				WorkflowLogsStack.Children.Add(new Label
				{
					Text = line,
					FontSize = 11,
					TextColor = Color.FromArgb("#5A6A8A"),
					LineBreakMode = LineBreakMode.WordWrap
				});
			}

			if (localDecisions.Count == 0)
			{
				return;
			}
		}

		if (logs.Count == 0 && _proposal is not null)
		{
			var fallback = new Label
			{
				Text = $"SUBMITTED: DRAFT → PENDING: {_proposal.SubmittedBy} · {_proposal.SubmittedDate:MMM dd, yyyy h:mm tt}",
				FontSize = 11,
				TextColor = Color.FromArgb("#5A6A8A"),
				LineBreakMode = LineBreakMode.WordWrap
			};
			WorkflowLogsStack.Children.Add(fallback);
			return;
		}

		foreach (var step in logs)
		{
			var logLabel = new Label
			{
				Text = $"{step.Status?.ToUpperInvariant()}: {step.RoleName}" +
				       (step.ReviewedBy is not null ? $" · {step.ReviewedBy}" : string.Empty) +
				       (step.ActedAt.HasValue ? $" · {step.ActedAt:MMM dd, yyyy}" : string.Empty),
				FontSize = 11,
				TextColor = Color.FromArgb("#5A6A8A"),
				LineBreakMode = LineBreakMode.WordWrap
			};
			WorkflowLogsStack.Children.Add(logLabel);
		}
	}

	private void QueueFieldReviewAutoSave()
	{
		if (_proposal is null)
		{
			return;
		}

		_fieldReviewAutoSaveCts?.Cancel();
		_fieldReviewAutoSaveCts = new CancellationTokenSource();
		var token = _fieldReviewAutoSaveCts.Token;

		_ = Task.Run(async () =>
		{
			try
			{
				await Task.Delay(450, token).ConfigureAwait(false);
				if (token.IsCancellationRequested)
				{
					return;
				}

				await MainThread.InvokeOnMainThreadAsync(async () =>
				{
					await AutoSaveFieldReviewDraftAsync().ConfigureAwait(true);
				});
			}
			catch (TaskCanceledException)
			{
			}
		}, token);
	}

	private async Task AutoSaveFieldReviewDraftAsync()
	{
		if (_proposal is null || _fieldReviewAutoSaveInFlight)
		{
			return;
		}

		var savableChanges = BuildFieldChanges(includeIncompleteRevisionNotes: false);
		if (savableChanges.Count == 0)
		{
			return;
		}

		_fieldReviewAutoSaveInFlight = true;
		try
		{
			var result = await _revisionService.SubmitFieldChangesAsync(_proposal.Id, savableChanges).ConfigureAwait(true);
			if (!result.Success)
			{
				return;
			}

			await RefreshHistoryEntriesAsync().ConfigureAwait(true);
			BuildWorkflowLogs();
		}
		finally
		{
			_fieldReviewAutoSaveInFlight = false;
		}
	}

	private List<FieldChange> BuildFieldChanges(bool includeIncompleteRevisionNotes)
	{
		var changes = new List<FieldChange>();
		for (var i = 0; i < _fields.Count; i++)
		{
			var field = _fields[i];
			if (field.State == FieldReviewState.Pending)
			{
				continue;
			}

			var status = field.State == FieldReviewState.Passed ? "passed" : "revision";
			var note = (field.RevisionNote ?? string.Empty).Trim();

			if (status == "revision" && string.IsNullOrWhiteSpace(note) && !includeIncompleteRevisionNotes)
			{
				continue;
			}

			changes.Add(new FieldChange
			{
				FieldKey = MakeStableFieldKey(field.StepKey, field.Label),
				FieldLabel = field.Label,
				Status = status,
				Comment = string.IsNullOrWhiteSpace(note) ? null : note
			});
		}

		return changes;
	}

	/// <summary>
	/// Stable API keys must be unique per proposal batch. Labels repeat across steps (e.g. two "Venue"
	/// rows) — Postgres UPSERT fails if two rows share the same conflict target (<c>field_key</c>).
	/// </summary>
	private static string MakeStableFieldKey(string stepKey, string label)
	{
		var step = string.IsNullOrWhiteSpace(stepKey) ? "step1" : stepKey.Trim().ToLowerInvariant();
		return $"{step}_{ToFieldKey(label)}";
	}

	private static string ToFieldKey(string label)
	{
		if (string.IsNullOrWhiteSpace(label))
		{
			return "field";
		}

		var chars = label
			.Trim()
			.ToLowerInvariant()
			.Select(c => char.IsLetterOrDigit(c) ? c : '_')
			.ToArray();
		var key = new string(chars);
		while (key.Contains("__", StringComparison.Ordinal))
		{
			key = key.Replace("__", "_", StringComparison.Ordinal);
		}
		return key.Trim('_');
	}

	private async Task RefreshHistoryEntriesAsync()
	{
		if (_proposal is null)
		{
			return;
		}

		_historyEntries = (await _revisionService.GetRevisionHistoryAsync(_proposal.Id).ConfigureAwait(true))
			.OrderByDescending(h => h.Timestamp)
			.ToList();
	}

	// ──────────────────────────────────────────────────────────────────────────
	// Submit field review
	// ──────────────────────────────────────────────────────────────────────────

	private async void OnSubmitFieldReviewClicked(object? sender, EventArgs e)
	{
		if (_proposal is null)
		{
			return;
		}

		SubmitFieldReviewBtn.IsEnabled = false;

		var undecided = _fields.Where(f => f.State == FieldReviewState.Pending).ToList();
		if (undecided.Count > 0)
		{
			await DisplayAlertAsync(
				"Incomplete field review",
				$"{undecided.Count} field(s) are still missing a decision. Mark each required field as Passed or Revision before submitting.",
				"OK");
			UpdateSubmitButtonState();
			return;
		}

		// Validate: revision fields must have a note
		var missing = _fields.Where(f => f.State == FieldReviewState.Revision
		                                 && string.IsNullOrWhiteSpace(f.RevisionNote)).ToList();
		if (missing.Count > 0)
		{
			await DisplayAlertAsync(
				"Revision notes required",
				$"{missing.Count} field(s) marked for revision still need a note:\n" +
				string.Join('\n', missing.Select(f => $"• {f.Label}")),
				"OK");
			UpdateSubmitButtonState();
			return;
		}

		var anyRevision = _fields.Any(f => f.State == FieldReviewState.Revision);
		var saveResult = await _revisionService.SubmitFieldChangesAsync(
			_proposal.Id,
			BuildFieldChanges(includeIncompleteRevisionNotes: true)).ConfigureAwait(true);
		if (!saveResult.Success)
		{
			await DisplayAlertAsync("Could not save field review", saveResult.Message ?? "Please try again.", "OK");
			UpdateSubmitButtonState();
			return;
		}
		await RefreshHistoryEntriesAsync().ConfigureAwait(true);
		BuildWorkflowLogs();

		if (anyRevision)
		{
			var remarks = string.Join('\n', _fields
				.Where(f => f.State == FieldReviewState.Revision)
				.Select(f => $"• {f.Label}: {f.RevisionNote}"));

			var result = await _approvalService.ReturnProposalAsync(_proposal.Id, remarks);
			if (!result.Success)
			{
				await DisplayAlertAsync("Could not return", result.Message ?? "Please try again.", "OK");
				UpdateSubmitButtonState();
				return;
			}

			await DisplayAlertAsync("Returned for revision", "Revision notes were sent to the RSO.", "OK");
			await Shell.Current.GoToAsync("//pendingapprovals");
			return;
		}
		else
		{
			var result = await _approvalService.ApproveProposalAsync(_proposal.Id);
			if (!result.Success)
			{
				await DisplayAlertAsync("Could not approve", result.Message ?? "Please try again.", "OK");
				UpdateSubmitButtonState();
				return;
			}

			await DisplayAlertAsync("Approved", "The proposal moves to the next stage.", "OK");
			await Shell.Current.GoToAsync("//pendingapprovals");
			return;
		}
	}

	// ──────────────────────────────────────────────────────────────────────────
	// Resubmit (RSO President)
	// ──────────────────────────────────────────────────────────────────────────

	private async void OnResubmitForReviewClicked(object? sender, EventArgs e)
	{
		if (_proposal is null)
		{
			return;
		}

		if (!IsReturned())
		{
			await DisplayAlertAsync("Not needed", "Resubmit is only for proposals returned for revision.", "OK");
			return;
		}

		// The proposal must go back to the signatory who returned it (whoever holds the
		// current stage), NOT the first stage. The backend keeps `current_stage` at that
		// signatory while the proposal is in "Returned for Revision"; we only flip the
		// status back to pending so it appears in their queue again.
		var returningStage = string.IsNullOrWhiteSpace(_proposal.CurrentStage)
			? ProposalWorkflowService.GetStages(_proposal.ApprovalFlowType)[0]
			: _proposal.CurrentStage;

		var result = await _proposalService.ResubmitProposalAsync(_proposal.Id);
		if (!result.Success)
		{
			await DisplayAlertAsync("Could not resubmit", result.Message ?? "Please try again.", "OK");
			return;
		}

		await DisplayAlertAsync("Resubmitted", $"The proposal is back at {returningStage}.", "OK");
		await Shell.Current.GoToAsync("//pendingapprovals");
	}

	// ──────────────────────────────────────────────────────────────────────────
	// Helpers
	// ──────────────────────────────────────────────────────────────────────────

	private bool IsFullyApproved() =>
		string.Equals(_proposal?.Status, "Fully Approved", StringComparison.OrdinalIgnoreCase);

	private bool IsReturned() =>
		string.Equals(_proposal?.Status, "Returned for Revision", StringComparison.OrdinalIgnoreCase);

	private bool IsProposalRejectedForWorkflow()
	{
		if (_proposal is null)
		{
			return false;
		}

		var n = Proposal.NormalizeStatus(_proposal.Status);
		return string.Equals(n, "Rejected", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// True when locally built workflow marks an explicit current step and it matches this reviewer.
	/// Used when API <see cref="Proposal.Status"/> is not one of our actionable labels but routing is active.
	/// </summary>
	private bool WorkflowCurrentStepMatchesReviewer(User? user)
	{
		if (user is null || _proposal is null || _steps.Count == 0)
		{
			return false;
		}

		var current = _steps.FirstOrDefault(s => s.IsCurrentStep);
		if (current is null || string.IsNullOrWhiteSpace(current.RoleName))
		{
			return false;
		}

		var hints = ApprovalRules.GetReviewerRoleHints(user);
		return hints.Any(h => ProposalWorkflowService.IsEquivalentRole(current.RoleName, h));
	}

	private int FindCurrentStepIndex()
	{
		if (_proposal is null || _steps.Count == 0)
		{
			return -1; // points to the Submitted virtual node
		}

		for (var i = 0; i < _steps.Count; i++)
		{
			if (_steps[i].IsCurrentStep)
			{
				return i;
			}
		}

		for (var i = 0; i < _steps.Count; i++)
		{
			if (ProposalWorkflowService.IsEquivalentRole(_steps[i].RoleName, _proposal.CurrentStage))
			{
				return i;
			}
		}

		return -1;
	}

	private void ApplyStatusBadge(string status)
	{
		var (bg, fg) = status.Trim() switch
		{
			"Approved" => (PassedBg, PassedText),
			"Fully Approved" => (PassedBg, PassedText),
			"Returned for Revision" => (RevisionBg, RevisionText),
			"Under Review" => (Color.FromArgb("#E8EEFF"), PrimaryColor),
			_ => (Colors.White, PrimaryColor)
		};
		StatusBadgeBorder.BackgroundColor = bg;
		StatusLabel.TextColor = fg;
	}

}
