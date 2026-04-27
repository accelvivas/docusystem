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
	private List<Proposal> allProposals = [];
	private string currentStatusFilter = "All";
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
			await LoadProposalsAsync();
			TryApplyQueryFilter();
			UpdateFilterUI();
			ApplyFilters();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(ex);
		}
	}

	private async void OnProposalsRefreshing(object? sender, EventArgs e)
	{
		try
		{
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
		allProposals = proposals
			.OrderByDescending(p => string.Equals(p.Status, "Returned for Revision", StringComparison.OrdinalIgnoreCase))
			.ThenByDescending(p => p.SubmittedDate)
			.ToList();
	}

	/// <summary>Maps <c>//pendingapprovals?filter=…</c> (e.g. from the dashboard) to the chip state.</summary>
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
			currentStatusFilter = "Pending";
		}
		else if (key.Equals("returned", StringComparison.OrdinalIgnoreCase))
		{
			currentStatusFilter = "Returned";
		}
		else if (key.Equals("all", StringComparison.OrdinalIgnoreCase) ||
		         key.Equals("browse", StringComparison.OrdinalIgnoreCase) ||
		         key.Equals("approved", StringComparison.OrdinalIgnoreCase) ||
		         key.Equals("fullyapproved", StringComparison.OrdinalIgnoreCase))
		{
			// "approved" from dashboard: show full list (fully approved may be excluded from this queue by the API).
			currentStatusFilter = "All";
		}
		else
		{
			currentStatusFilter = "All";
		}
	}

	private void DisplayProposals(List<Proposal> proposalsToDisplay)
	{
		ProposalsStack.Children.Clear();

		if (proposalsToDisplay.Count == 0)
		{
			var emptyMsg = string.Equals(_session.CurrentUser?.Role, "RSO President", StringComparison.OrdinalIgnoreCase)
				? "No proposals for your student organization in this list.\n\nIf you just signed in, confirm you are using the account that matches your org. Pull down to refresh."
				: "Nothing to review right now.\n\nPull down to refresh.";
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
			var proposalCard = CreateProposalCard(proposal);
			ProposalsStack.Children.Add(proposalCard);
		}
	}

	private Frame CreateProposalCard(Proposal proposal)
	{
		var statusColor = proposal.Status switch
		{
			"Under Review" => Ui.Navy,
			"Returned for Revision" => Ui.White,
			"Fully Approved" => Ui.White,
			"Submitted" => Ui.Navy,
			"Draft" => Ui.Navy,
			_ => Ui.Navy
		};

		var statusBg = proposal.Status switch
		{
			"Under Review" => Ui.NavyWash,
			"Returned for Revision" => Ui.Navy,
			"Fully Approved" => Ui.Navy,
			"Submitted" => Ui.White,
			"Draft" => Ui.NavyWash,
			_ => Ui.NavyWash
		};

		var card = new Frame
		{
			CornerRadius = 14,
			BorderColor = Ui.NavyLine,
			HasShadow = false,
			Padding = 14,
			BackgroundColor = Ui.White,
			Content = new VerticalStackLayout
			{
				Spacing = 10,
				Children =
				{
					new VerticalStackLayout
					{
						Spacing = 3,
						Children =
						{
							new Label
							{
								Text = proposal.Title,
								FontSize = 14,
								FontAttributes = FontAttributes.Bold,
								TextColor = Ui.Navy
							},
							new Label
							{
								Text = proposal.OrganizationName,
								FontSize = 12,
								TextColor = Ui.NavyMutedText
							},
							new Label
							{
								Text = $"Event type: {ProposalWorkflowService.GetEventTypeDisplay(proposal.ApprovalFlowType)}",
								FontSize = 11,
								TextColor = Ui.NavyMutedText,
								LineBreakMode = LineBreakMode.WordWrap
							}
						}
					},
					new HorizontalStackLayout
					{
						Spacing = 10,
						Children =
						{
							new VerticalStackLayout
							{
								Spacing = 1,
								Children =
								{
									new Label
									{
										Text = "CURRENT STAGE",
										FontSize = 9,
										TextColor = Ui.NavyMutedText,
										FontAttributes = FontAttributes.Bold
									},
									new Label
									{
										Text = proposal.CurrentStage,
										FontSize = 12,
										FontAttributes = FontAttributes.Bold,
										TextColor = Ui.Navy
									}
								}
							},
							new VerticalStackLayout
							{
								Spacing = 1,
								Children =
								{
									new Label
									{
										Text = "STATUS",
										FontSize = 9,
										TextColor = Ui.NavyMutedText,
										FontAttributes = FontAttributes.Bold
									},
									new Frame
									{
										Padding = new Thickness(7, 3),
										CornerRadius = 6,
										BorderColor = Ui.NavyLine,
										HasShadow = false,
										BackgroundColor = statusBg,
										Content = new Label
										{
											Text = proposal.Status,
											FontSize = 11,
											FontAttributes = FontAttributes.Bold,
											TextColor = statusColor
										}
									}
								}
							}
						}
					},
					new Label
					{
						Text = $"Submitted: {proposal.SubmittedDate:MMM dd, yyyy}",
						FontSize = 11,
						TextColor = Ui.NavyMutedText
					},
					new Button
					{
						Text = "Open proposal details",
						FontSize = 12,
						FontAttributes = FontAttributes.Bold,
						TextColor = Ui.White,
						BackgroundColor = Ui.Navy,
						BorderColor = Ui.NavyLine,
						BorderWidth = 1,
						CornerRadius = 10,
						Padding = new Thickness(0, 11),
						CommandParameter = proposal
					}
				}
			}
		};

		var button = (Button)((VerticalStackLayout)card.Content).Children.Last();
		button.Clicked += async (_, _) => await OpenProposalDetailsAsync(proposal);

		return card;
	}

	/// <summary>Loads the latest proposal snapshot, stores it in session, and opens the contextual details page.</summary>
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

	private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
	{
		ApplyFilters();
	}

	private void OnStatusFilterClicked(object? sender, EventArgs e)
	{
		if (sender is Button button)
		{
			currentStatusFilter = button.ClassId;
			UpdateFilterUI();
			ApplyFilters();
		}
	}

	private void UpdateFilterUI()
	{
		ResetStatusFilterButtons();

		var activeButton = currentStatusFilter switch
		{
			"All" => FilterAllStatusBtn,
			"Pending" => FilterPendingStatusBtn,
			"Returned" => FilterReturnedStatusBtn,
			_ => FilterAllStatusBtn
		};

		activeButton.BackgroundColor = Ui.Navy;
		activeButton.TextColor = Ui.White;
	}

	private void ResetStatusFilterButtons()
	{
		FilterAllStatusBtn.BackgroundColor = Ui.NavyWash;
		FilterAllStatusBtn.TextColor = Ui.Navy;

		FilterPendingStatusBtn.BackgroundColor = Ui.NavyWash;
		FilterPendingStatusBtn.TextColor = Ui.Navy;

		FilterReturnedStatusBtn.BackgroundColor = Ui.NavyWash;
		FilterReturnedStatusBtn.TextColor = Ui.Navy;
	}

	private void ApplyFilters()
	{
		var searchText = SearchEntry.Text?.ToLower() ?? string.Empty;

		var filtered = allProposals
			.Where(p =>
			{
				var matchesSearch = string.IsNullOrEmpty(searchText) ||
					p.Title.ToLower().Contains(searchText) ||
					p.OrganizationName.ToLower().Contains(searchText);

				var matchesStatus = currentStatusFilter == "All" ||
					(currentStatusFilter == "Pending" &&
					 (string.Equals(p.Status, "Under Review", StringComparison.OrdinalIgnoreCase) ||
					  string.Equals(p.Status, "Submitted", StringComparison.OrdinalIgnoreCase))) ||
					(currentStatusFilter == "Returned" && string.Equals(p.Status, "Returned for Revision", StringComparison.OrdinalIgnoreCase));

				return matchesSearch && matchesStatus;
			})
			.ToList();

		DisplayProposals(filtered);
	}

	private async void OnOpenMockProposalClicked(object? sender, EventArgs e)
	{
		var mock = new Proposal
		{
			Id = 999001,
			Title = "DOTA Tournament",
			OrganizationName = "Hacker Team",
			SubmittedBy = "Alcantara Kid",
			CurrentStage = "Adviser",
			Status = "Under Review",
			ActivityDate = DateTime.Today.AddDays(3),
			Venue = "Gym",
			Budget = 100000m,
			Description = "Campus-wide e-sports event focused on teamwork, strategy, and student engagement.",
			SubmittedDate = DateTime.Today.AddDays(-1),
			CanApprove = true,
			CanEdit = false,
			ApprovalFlowType = ApprovalFlowType.Academic
		};

		_session.SetSelectedProposal(mock);
		await Shell.Current.GoToAsync("proposaldetails");
	}
}
