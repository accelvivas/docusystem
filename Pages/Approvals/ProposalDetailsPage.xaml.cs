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
	private readonly IProposalService _proposalService;
	private readonly IApprovalService _approvalService;
	private readonly IRevisionService _revisionService;

	private Proposal? _proposal;
	private List<ApprovalStep> _steps = [];
	private List<ProposalFieldReview> _fields = [];

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
		await LoadAsync();
	}

	// ──────────────────────────────────────────────────────────────────────────
	// Load
	// ──────────────────────────────────────────────────────────────────────────

	private async Task LoadAsync()
	{
		_proposal = _session.SelectedProposal;
		if (_proposal is null)
		{
			// UI preview fallback: load a mock proposal so approvers can review the new layout immediately.
			_proposal = CreateMockProposal();
			_session.SetSelectedProposal(_proposal);
		}

		var refreshed = await _proposalService.GetProposalByIdAsync(_proposal.Id);
		if (refreshed is not null)
		{
			_proposal = refreshed;
			_session.SetSelectedProposal(refreshed);
		}

		// Recompute flow from real payload wording (Curricular / Non-curricular)
		// so stage routing matches backend proposal type.
		_proposal.ApprovalFlowType = ProposalWorkflowService.InferFlowTypeFromProposal(_proposal);

		_steps = _approvalService.BuildApprovalSteps(_proposal).ToList();
		_fields = BuildFieldList(_proposal);
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
	}

	private static Proposal CreateMockProposal()
	{
		var activityDate = DateTime.Today.AddDays(3);
		return new Proposal
		{
			Id = 999001,
			Title = "DOTA Tournament",
			OrganizationName = "Hacker Team",
			SubmittedBy = "Alcantara Kid",
			CurrentStage = "Adviser",
			Status = "Under Review",
			ActivityDate = activityDate,
			Venue = "Gym",
			Budget = 100000m,
			Description = "Campus-wide e-sports event focused on teamwork, strategy, and student engagement.",
			SubmittedDate = DateTime.Today.AddDays(-1),
			CanApprove = true,
			CanEdit = false,
			ApprovalFlowType = ApprovalFlowType.Academic
		};
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

		var isPresident = string.Equals(user?.Role, "RSO President", StringComparison.OrdinalIgnoreCase);
		ResubmitBtn.IsVisible = isPresident && isReturned;

		ApprovalRules.ApplyWorkflowPermissions(_proposal, user);
		var canApprove = !isFullyApproved && ApprovalRules.CanApprove(user, _proposal);
		SubmitFieldReviewBtn.IsVisible = canApprove;
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

	private static List<ProposalFieldReview> BuildFieldList(Proposal p)
	{
		var ext = p.ExtraData;
		var actDate = p.ActivityDate == default ? "—" : p.ActivityDate.ToString("MMM dd, yyyy");
		var endDate = p.ActivityDate == default ? "—" : p.ActivityDate.AddDays(1).ToString("MMM dd, yyyy");
		var proposedDates = FirstNonEmpty(
			GetExtraString(ext, "proposed_dates"),
			GetExtraString(ext, "proposal_dates"),
			$"{actDate} – {endDate}");
		var proposedTime = FirstNonEmpty(
			GetExtraString(ext, "proposed_time"),
			GetExtraString(ext, "activity_time"),
			"—");
		var academicYear = FirstNonEmpty(GetExtraString(ext, "academic_year"), "2026-2027");
		var department = FirstNonEmpty(GetExtraString(ext, "department"), "—");
		var program = FirstNonEmpty(GetExtraString(ext, "program"), "—");
		var overallGoal = FirstNonEmpty(GetExtraString(ext, "overall_goal"), p.Description);
		var specificObjectives = FirstNonEmpty(GetExtraString(ext, "specific_objectives"), "—");
		var criteriaMechanics = FirstNonEmpty(GetExtraString(ext, "criteria_mechanics"), "—");
		var programFlow = FirstNonEmpty(GetExtraString(ext, "program_flow"), "—");
		var sourceOfFunding = FirstNonEmpty(GetExtraString(ext, "source_of_funding"), "RSO Fund");

		var budgetRows = GetBudgetRows(ext, p.Budget);
		var totalFromRows = budgetRows.Sum(r => r.Price);
		var budgetTotal = totalFromRows > 0 ? totalFromRows : p.Budget;

		return
		[
			// ── Section 1: Submission overview ─────────────────────────────
			new() { StepKey = "step1", Label = "Proposal Option",       Value = "Activity not in submitted calendar" },
			new() { StepKey = "step1", Label = "RSO Name",              Value = p.OrganizationName },
			new() { StepKey = "step1", Label = "Title of Activity",     Value = p.Title },
			new() { StepKey = "step1", Label = "Partner Entities",      Value = "—" },
			new() { StepKey = "step1", Label = "Nature of Activity",    Value = "Non-curricular" },
			new() { StepKey = "step1", Label = "Type of Activity",      Value = "Competition" },
			new() { StepKey = "step1", Label = "Target SDG",            Value = "SDG 4, SDG 8" },
			new() { StepKey = "step1", Label = "Step 1 Proposed Budget",Value = $"PHP {p.Budget:N2}" },
			new() { StepKey = "step1", Label = "Step 1 Budget Source",  Value = "RSO Fund" },
			new() { StepKey = "step1", Label = "Date of Activity",      Value = actDate },
			new() { StepKey = "step1", Label = "Venue",                 Value = string.IsNullOrWhiteSpace(p.Venue) ? "—" : p.Venue },
			new() { StepKey = "step1", Label = "Upload Request Letter", Value = "Submitted file attached.", IsFile = true },
			new() { StepKey = "step1", Label = "Resume of Speaker",     Value = "Submitted file attached.", IsFile = true },
			new() { StepKey = "step1", Label = "Sample Post-Survey Form", Value = "Submitted file attached.", IsFile = true },

			// ── Step 2: Proposal Submission ────────────────────────────────
			new() { StepKey = "step2", Label = "Organization Logo",     Value = "Submitted file attached.", IsFile = true },
			new() { StepKey = "step2", Label = "Organization",          Value = p.OrganizationName },
			new() { StepKey = "step2", Label = "Academic Year",         Value = academicYear },
			new() { StepKey = "step2", Label = "Department",            Value = department },
			new() { StepKey = "step2", Label = "Program",               Value = program },
			new() { StepKey = "step2", Label = "Project / Activity Title", Value = p.Title },
			new() { StepKey = "step2", Label = "Proposed Dates",        Value = proposedDates },
			new() { StepKey = "step2", Label = "Proposed Time",         Value = proposedTime },
			new() { StepKey = "step2", Label = "Venue",                 Value = string.IsNullOrWhiteSpace(p.Venue) ? "—" : p.Venue },
			new() { StepKey = "step2", Label = "Overall Goal",          Value = overallGoal },
			new() { StepKey = "step2", Label = "Specific Objectives",   Value = specificObjectives },
			new() { StepKey = "step2", Label = "Criteria / Mechanics",  Value = criteriaMechanics },
			new() { StepKey = "step2", Label = "Program Flow",          Value = programFlow },
			new() { StepKey = "step2", Label = "Proposed Budget (Total)", Value = $"PHP {budgetTotal:N2}" },
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
			_ => null
		};
	}

	private static List<BudgetTableRow> GetBudgetRows(Dictionary<string, JsonElement>? ext, decimal fallbackBudget)
	{
		if (ext is not null)
		{
			var keys = new[] { "detailed_budget_table", "budget_rows", "budget_table", "budget_items", "materials" };
			for (var i = 0; i < keys.Length; i++)
			{
				if (TryParseBudgetRows(ext, keys[i], out var rows) && rows.Count > 0)
				{
					return rows;
				}
			}
		}

		return
		[
			new BudgetTableRow
			{
				Material = "pc",
				Quantity = 10.00m,
				UnitPrice = fallbackBudget <= 0 ? 0 : fallbackBudget / 10.00m
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
			ColumnDefinitions =
			{
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Auto),
				new ColumnDefinition(GridLength.Auto)
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
			var fileLink = new Label
			{
				Text = "Open / Download file ↗",
				FontSize = 11,
				TextColor = PrimaryColor,
				TextDecorations = TextDecorations.Underline
			};
			labelStack.Children.Add(fileLink);
		}

		Grid.SetColumn(labelStack, 0);

		// Passed button
		var passedBtn = CreateReviewButton("Passed", index, isPassed: true, field.State == FieldReviewState.Passed);
		Grid.SetColumn(passedBtn, 1);

		// Revision button
		var revisionBtn = CreateReviewButton("Revision", index, isPassed: false, field.State == FieldReviewState.Revision);
		Grid.SetColumn(revisionBtn, 2);

		topRow.Children.Add(labelStack);
		topRow.Children.Add(passedBtn);
		topRow.Children.Add(revisionBtn);
		cardContent.Children.Add(topRow);

		// --- Revision note input (visible only when Revision selected) ---
		var revisionNoteSection = new VerticalStackLayout { Spacing = 4, IsVisible = field.RevisionInputVisible };

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

	private Border CreateReviewButton(string text, int fieldIndex, bool isPassed, bool isActive)
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
			MinimumWidthRequest = 64
		};

		var tapGesture = new TapGestureRecognizer();
		tapGesture.Tapped += (_, _) => OnFieldReviewButtonTapped(fieldIndex, isPassed);
		btn.GestureRecognizers.Add(tapGesture);

		return btn;
	}

	private void OnFieldReviewButtonTapped(int index, bool isPassed)
	{
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
		var displaySelectedIndex = _selectedStepIndex + 1; // +1 because Submitted is at index 0

		for (var i = 0; i < stageNames.Length; i++)
		{
			var stage = stageNames[i];
			// "Submitted" is display index 0 — always completed.
			// Signatory steps start at display index 1, mapped to _steps[i-1].
			var stepIdx = i - 1;
			var step = (stepIdx >= 0 && stepIdx < _steps.Count) ? _steps[stepIdx] : null;
			var isCurrent = i == displaySelectedIndex;
			var isCompleted = i == 0 || step?.Status == "Completed";
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
				Content = circleContent
			};

			var tap = new TapGestureRecognizer();
			tap.Tapped += (_, _) =>
			{
				// Map display index back to _steps index (subtract 1 for Submitted node).
				_selectedStepIndex = Math.Max(0, isCaptured - 1);
				BuildWorkflowTrack();
				BindSelectedStageCard();
			};
			circleBorder.GestureRecognizers.Add(tap);

			var stageLabel = new Label
			{
				Text = stage,
				FontSize = 9,
				TextColor = isCompleted ? Color.FromArgb("#2AA060") : isCurrent ? PrimaryColor : Color.FromArgb("#8090A8"),
				FontAttributes = isCurrent ? FontAttributes.Bold : FontAttributes.None,
				HorizontalTextAlignment = TextAlignment.Center,
				MaximumWidthRequest = 60,
				LineBreakMode = LineBreakMode.WordWrap
			};

			var nodeStack = new VerticalStackLayout
			{
				Spacing = 4,
				HorizontalOptions = LayoutOptions.Center,
				MinimumWidthRequest = 62
			};
			nodeStack.Children.Add(circleBorder);
			nodeStack.Children.Add(stageLabel);

			nodeStack.GestureRecognizers.Add(tap);

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

		var logs = _steps
			.Where(s => s.Status == "Completed" || s.ActedAt.HasValue)
			.OrderByDescending(s => s.ActedAt)
			.Take(5)
			.ToList();

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

	// ──────────────────────────────────────────────────────────────────────────
	// Submit field review
	// ──────────────────────────────────────────────────────────────────────────

	private async void OnSubmitFieldReviewClicked(object? sender, EventArgs e)
	{
		if (_proposal is null)
		{
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
			return;
		}

		var anyRevision = _fields.Any(f => f.State == FieldReviewState.Revision);

		var isMock = _proposal.Id == 999001;

		if (anyRevision)
		{
			var remarks = string.Join('\n', _fields
				.Where(f => f.State == FieldReviewState.Revision)
				.Select(f => $"• {f.Label}: {f.RevisionNote}"));

			if (!isMock)
			{
				var result = await _approvalService.ReturnProposalAsync(_proposal.Id, remarks);
				if (!result.Success)
				{
					await DisplayAlertAsync("Could not return", result.Message ?? "Please try again.", "OK");
					return;
				}
			}

			_proposal.Status = "Returned for Revision";
			_proposal.LastRemarks = remarks;
			await DisplayAlertAsync("Returned for revision", "Revision notes were sent to the RSO.", "OK");
		}
		else
		{
			if (!isMock)
			{
				var result = await _approvalService.ApproveProposalAsync(_proposal.Id);
				if (!result.Success)
				{
					await DisplayAlertAsync("Could not approve", result.Message ?? "Please try again.", "OK");
					return;
				}
			}

			// Advance to the next stage locally so the UI updates immediately.
			var currentIdx = FindCurrentStepIndex();
			if (currentIdx >= 0 && currentIdx < _steps.Count - 1)
			{
				_proposal.CurrentStage = _steps[currentIdx + 1].RoleName;
				_proposal.Status = "Approved";
			}
			else
			{
				_proposal.Status = "Fully Approved";
				_proposal.FullyApprovedAt = DateTime.Now;
			}

			await DisplayAlertAsync("Approved", "The proposal moves to the next stage.", "OK");
		}

		_session.SetSelectedProposal(_proposal);
		await LoadAsync();
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

		var firstStage = ProposalWorkflowService.GetStages(_proposal.ApprovalFlowType)[0];
		_proposal.Status = "Under Review";
		_proposal.CurrentStage = firstStage;
		var result = await _proposalService.UpdateProposalAsync(_proposal);
		if (!result.Success)
		{
			await DisplayAlertAsync("Could not resubmit", result.Message ?? "Please try again.", "OK");
			return;
		}

		await DisplayAlertAsync("Resubmitted", $"The proposal is back at {firstStage}.", "OK");
		await LoadAsync();
	}

	// ──────────────────────────────────────────────────────────────────────────
	// Helpers
	// ──────────────────────────────────────────────────────────────────────────

	private bool IsFullyApproved() =>
		string.Equals(_proposal?.Status, "Fully Approved", StringComparison.OrdinalIgnoreCase);

	private bool IsReturned() =>
		string.Equals(_proposal?.Status, "Returned for Revision", StringComparison.OrdinalIgnoreCase);

	private int FindCurrentStepIndex()
	{
		if (_proposal is null || _steps.Count == 0)
		{
			return -1; // points to the Submitted virtual node
		}

		for (var i = 0; i < _steps.Count; i++)
		{
			if (string.Equals(_steps[i].RoleName, _proposal.CurrentStage, StringComparison.OrdinalIgnoreCase))
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
