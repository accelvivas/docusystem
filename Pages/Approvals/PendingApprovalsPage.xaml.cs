namespace docusystem.Pages.Approvals;

using docusystem.Models;
using docusystem.Services;
using Ui = docusystem.UiBrand;

/// <summary>
/// Lists proposals pending action — data from <see cref="IProposalService.GetPendingApprovalsAsync"/> (TODO: Laravel).
/// </summary>
public partial class PendingApprovalsPage : ContentPage, IQueryAttributable
{
	private readonly AppSessionService _session;
	private readonly IProposalService _proposalService;
	private List<Proposal> _allProposals = [];
	private string? _pendingQueryFilter;

	public PendingApprovalsPage(AppSessionService session, IProposalService proposalService)
	{
		InitializeComponent();
		_session = session;
		_proposalService = proposalService;
	}

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query is null)
		{
			return;
		}

		if (!query.TryGetValue("filter", out var raw) || raw is null)
		{
			return;
		}

		_pendingQueryFilter = raw switch
		{
			string s => s,
			_ => raw.ToString()
		};
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		try
		{
			ApplyApproverHeaderCopy();
			SetLoadingState(true);
			InitializeFilterDefaults();
			await LoadProposalsAsync();
			TryApplyQueryFilter();
			ApplyFilters();
			SetLoadingState(false);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(ex);
			SetLoadingState(false);
			ErrorStateLabel.Text = "Could not load pending approvals. Pull down to retry.";
			ErrorStateLabel.IsVisible = true;
		}
	}

	private async void OnProposalsRefreshing(object? sender, EventArgs e)
	{
		try
		{
			ErrorStateLabel.IsVisible = false;
			await LoadProposalsAsync();
			ApplyFilters();
		}
		finally
		{
			ProposalsRefresh.IsRefreshing = false;
		}
	}

	private async Task LoadProposalsAsync()
	{
		var currentUser = _session.CurrentUser;
		if (currentUser is null)
		{
			return;
		}

		var proposals = (await _proposalService.GetPendingApprovalsAsync()).ToList();

		_allProposals = proposals
			.OrderByDescending(p => string.Equals(p.Status, "Returned for Revision", StringComparison.OrdinalIgnoreCase))
			.ThenByDescending(p => p.SubmittedDate)
			.ToList();
	}

	private void ApplyApproverHeaderCopy()
	{
		Title = "Pending Approvals";
		PendingPageTitleLabel.Text = "My Pending Approvals";
		PendingPageSubtitleLabel.Text = "Only documents currently routed to your role are shown.";
	}

	private void InitializeFilterDefaults()
	{
		if (StatusPicker.SelectedIndex < 0)
		{
			StatusPicker.SelectedIndex = 0;
		}
	}

	/// <summary>Maps <c>//pendingapprovals?filter=…</c> (e.g. from dashboard) to the status picker.</summary>
	private void TryApplyQueryFilter()
	{
		if (string.IsNullOrWhiteSpace(_pendingQueryFilter))
		{
			return;
		}

		var key = _pendingQueryFilter.Trim();
		_pendingQueryFilter = null;
		SearchEntry.Text = string.Empty;

		if (key.Equals("pending", StringComparison.OrdinalIgnoreCase) ||
		    key.Equals("myreview", StringComparison.OrdinalIgnoreCase) ||
		    key.Equals("active", StringComparison.OrdinalIgnoreCase))
		{
			StatusPicker.SelectedIndex = 1; // Pending
		}
		else if (key.Equals("returned", StringComparison.OrdinalIgnoreCase))
		{
			StatusPicker.SelectedIndex = 3; // Returned / Rejected
		}
		else if (key.Equals("all", StringComparison.OrdinalIgnoreCase) ||
		         key.Equals("browse", StringComparison.OrdinalIgnoreCase) ||
		         key.Equals("approved", StringComparison.OrdinalIgnoreCase) ||
		         key.Equals("fullyapproved", StringComparison.OrdinalIgnoreCase))
		{
			StatusPicker.SelectedIndex = 0; // All
		}
	}

	private void DisplayProposals(List<Proposal> proposalsToDisplay)
	{
		ProposalsStack.Children.Clear();

		if (proposalsToDisplay.Count == 0)
		{
			var emptyMsg = "Nothing to review right now.\n\nPull down to refresh.";
			ProposalsStack.Children.Add(
				new Label
				{
					Text = emptyMsg,
					FontSize = 14,
					TextColor = Ui.NavyMutedText,
					HorizontalOptions = LayoutOptions.Center,
					HorizontalTextAlignment = TextAlignment.Center,
					LineBreakMode = LineBreakMode.WordWrap,
					Margin = new Thickness(12, 40, 12, 0)
				}
			);
			return;
		}

		foreach (var proposal in proposalsToDisplay)
		{
			var proposalCard = CreateProposalRowCard(proposal);
			ProposalsStack.Children.Add(proposalCard);
		}
	}

	private Border CreateProposalRowCard(Proposal proposal)
	{
		var (statusText, statusBg, statusFg, statusBorder) = GetStatusVisuals(proposal.Status);
		var timingValue = GetPendingForDays(proposal);
		var timingLabel = "PENDING FOR";

		var card = new Border
		{
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
			Stroke = Color.FromArgb("#D2DDF0"),
			StrokeThickness = 1,
			Padding = 14,
			BackgroundColor = Colors.White,
			Content = new VerticalStackLayout
			{
				Spacing = 10,
				Children =
				{
					new Label
					{
						Text = string.IsNullOrWhiteSpace(proposal.Title) ? "Untitled proposal" : proposal.Title,
						FontSize = 17,
						FontAttributes = FontAttributes.Bold,
						TextColor = Ui.Navy,
						LineBreakMode = LineBreakMode.WordWrap
					},
					new Label
					{
						Text = string.IsNullOrWhiteSpace(proposal.OrganizationName) ? "—" : proposal.OrganizationName,
						FontSize = 13,
						TextColor = Color.FromArgb("#3F5C8F")
					},
					new Grid
					{
						ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Star) },
						ColumnSpacing = 8,
						Children =
						{
							BuildBadge("CURRENT STAGE", proposal.CurrentStage, Color.FromArgb("#EAF0FC"), Ui.Navy, Color.FromArgb("#C9D8F0")).Assign(gridColumn: 0),
							BuildBadge("STATUS", statusText, statusBg, statusFg, statusBorder).Assign(gridColumn: 1)
						}
					},
					BuildInfoLine(timingLabel, timingValue),
					new Grid
					{
						ColumnDefinitions = new ColumnDefinitionCollection
						{
							new(GridLength.Star),
							new(GridLength.Auto)
						},
						ColumnSpacing = 8,
						Children =
						{
							new Label
							{
								Text = string.Empty
							}.Assign(gridColumn: 0),
							new Button
							{
								Text = "View / Review",
								FontSize = 12,
								FontAttributes = FontAttributes.Bold,
								TextColor = Ui.Navy,
								BackgroundColor = Colors.White,
								BorderColor = Ui.Navy,
								BorderWidth = 1.5,
								CornerRadius = 10,
								Padding = new Thickness(14,8),
								CommandParameter = proposal
							}.Assign(gridColumn:1)
						}
					}
				}
			}
		};

		var actionButton = FindActionButton(card);
		if (actionButton is not null)
		{
			actionButton.Clicked += async (_, _) => await OpenProposalDetailsAsync(proposal);
		}

		return card;
	}

	private static Button? FindActionButton(Border card)
	{
		if (card.Content is not VerticalStackLayout root || root.Children.Count == 0)
		{
			return null;
		}

		var actionRow = root.Children.LastOrDefault() as Grid;
		return actionRow?.Children.OfType<Button>().FirstOrDefault();
	}

	private static View BuildBadge(string label, string value, Color bg, Color fg, Color border)
	{
		return new Border
		{
			BackgroundColor = bg,
			Stroke = border,
			StrokeThickness = 1,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
			Padding = new Thickness(10, 6),
			Content = new VerticalStackLayout
			{
				Spacing = 1,
				Children =
				{
					new Label
					{
						Text = label,
						FontSize = 9,
						FontAttributes = FontAttributes.Bold,
						TextColor = fg,
						Opacity = 0.8
					},
					new Label
					{
						Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
						FontSize = 11,
						FontAttributes = FontAttributes.Bold,
						TextColor = fg,
						LineBreakMode = LineBreakMode.TailTruncation
					}
				}
			}
		};
	}

	private static View BuildInfoLine(string label, string value) =>
		new HorizontalStackLayout
		{
			Spacing = 6,
			Children =
			{
				new Label
				{
					Text = $"{label}:",
					FontSize = 11,
					FontAttributes = FontAttributes.Bold,
					TextColor = Color.FromArgb("#46618D")
				},
				new Label
				{
					Text = value,
					FontSize = 12,
					TextColor = Color.FromArgb("#0F2A5F")
				}
			}
		};

	private static string GetPendingForDays(Proposal proposal)
	{
		if (proposal.SubmittedDate == default)
		{
			return "—";
		}

		var days = Math.Max(0, (DateTime.Today - proposal.SubmittedDate.Date).Days);
		return days == 1 ? "1 day" : $"{days} days";
	}

	private static (string Text, Color Bg, Color Fg, Color Border) GetStatusVisuals(string status)
	{
		var s = (status ?? string.Empty).Trim();
		if (string.Equals(s, "Returned for Revision", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(s, "Rejected", StringComparison.OrdinalIgnoreCase))
		{
			return ("RETURNED", Color.FromArgb("#FFF7E5"), Color.FromArgb("#8A6400"), Color.FromArgb("#D4AF37"));
		}

		if (string.Equals(s, "Approved", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(s, "Fully Approved", StringComparison.OrdinalIgnoreCase))
		{
			return ("APPROVED", Color.FromArgb("#003087"), Colors.White, Color.FromArgb("#002A78"));
		}

		return ("PENDING", Color.FromArgb("#FFF7E5"), Color.FromArgb("#8A6400"), Color.FromArgb("#D4AF37"));
	}

	private static string GetSelectedStatusFilter(Picker picker)
	{
		if (picker.SelectedIndex < 0 || picker.SelectedIndex >= picker.Items.Count)
		{
			return "All";
		}

		return picker.Items[picker.SelectedIndex];
	}

	private static bool MatchesStatusFilter(Proposal proposal, string selectedStatus)
	{
		var s = proposal.Status ?? string.Empty;
		return selectedStatus switch
		{
			"Pending" => string.Equals(s, "Under Review", StringComparison.OrdinalIgnoreCase) ||
			             string.Equals(s, "Submitted", StringComparison.OrdinalIgnoreCase) ||
			             string.Equals(s, "Pending", StringComparison.OrdinalIgnoreCase),
			"Approved" => string.Equals(s, "Approved", StringComparison.OrdinalIgnoreCase) ||
			              string.Equals(s, "Fully Approved", StringComparison.OrdinalIgnoreCase),
			"Returned / Rejected" => string.Equals(s, "Returned for Revision", StringComparison.OrdinalIgnoreCase) ||
			                         string.Equals(s, "Rejected", StringComparison.OrdinalIgnoreCase),
			_ => true
		};
	}

	/// <summary>Loads the latest proposal snapshot, stores it in session, and opens contextual details page.</summary>
	private async Task OpenProposalDetailsAsync(Proposal proposal)
	{
		_session.SetSelectedProposal(proposal);
		var refreshed = await _proposalService.GetProposalByIdAsync(proposal.Id);
		if (refreshed is not null)
		{
			_session.SetSelectedProposal(refreshed);
		}

		await Shell.Current.GoToAsync("proposaldetails");
	}

	private void OnFilterChanged(object? sender, EventArgs e) => ApplyFilters();

	private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilters();

	private void OnResetFiltersClicked(object? sender, EventArgs e)
	{
		StatusPicker.SelectedIndex = 0;
		SearchEntry.Text = string.Empty;
		ApplyFilters();
	}

	private void ApplyFilters()
	{
		var statusFilter = GetSelectedStatusFilter(StatusPicker);
		var search = (SearchEntry.Text ?? string.Empty).Trim().ToLowerInvariant();

		var filtered = _allProposals
			.Where(p =>
			{
				var matchesSearch = string.IsNullOrEmpty(search) ||
				                    (p.OrganizationName ?? string.Empty).ToLowerInvariant().Contains(search) ||
				                    (p.Title ?? string.Empty).ToLowerInvariant().Contains(search);

				var matchesStatus = MatchesStatusFilter(p, statusFilter);
				return matchesSearch && matchesStatus;
			})
			.ToList();

		DisplayProposals(filtered);
	}

	private void SetLoadingState(bool loading)
	{
		LoadingIndicator.IsVisible = loading;
		LoadingIndicator.IsRunning = loading;
		if (loading)
		{
			ErrorStateLabel.IsVisible = false;
		}
	}

}

internal static class PendingApprovalUiExtensions
{
	public static T Assign<T>(this T view, int? gridColumn = null, int? gridRow = null)
		where T : View
	{
		if (gridColumn.HasValue)
		{
			Grid.SetColumn(view, gridColumn.Value);
		}

		if (gridRow.HasValue)
		{
			Grid.SetRow(view, gridRow.Value);
		}

		return view;
	}
}
