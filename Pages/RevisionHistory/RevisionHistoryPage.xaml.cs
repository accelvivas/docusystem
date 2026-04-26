namespace docusystem.Pages.RevisionHistory;

using docusystem.Models;
using docusystem.Services;
using Ui = docusystem.UiBrand;

/// <summary>
/// Revision timeline — data from <see cref="IRevisionService.GetRevisionHistoryAsync"/> (TODO: Laravel).
/// </summary>
public partial class RevisionHistoryPage : ContentPage
{
	private readonly AppSessionService _session;
	private readonly IRevisionService _revisionService;

	public RevisionHistoryPage(AppSessionService session, IRevisionService revisionService)
	{
		InitializeComponent();
		_session = session;
		_revisionService = revisionService;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await LoadRevisionHistoryAsync();
	}

	private async Task LoadRevisionHistoryAsync()
	{
		var selectedProposal = _session.SelectedProposal;
		if (selectedProposal is null)
		{
			LatestRevisionSummaryBorder.IsVisible = false;
			RevisionContextHintLabel.Text =
				"Open a proposal from Pending Approvals, then open revision history from its details.";
			RevisionTimelineStack.Children.Clear();
			RevisionTimelineStack.Children.Add(CreateEmptyLabel(
				"No proposal selected.\n\nChoose one from Pending Approvals first."));
			return;
		}

		LatestRevisionSummaryBorder.IsVisible = true;
		RevisionContextHintLabel.Text =
			$"{selectedProposal.Title} · {selectedProposal.OrganizationName} · {selectedProposal.Status}";

		// TODO: GET /api/proposals/{id}/revisions (match your Laravel routes)
		var revisions = (await _revisionService.GetRevisionHistoryAsync(selectedProposal.Id))
			.OrderByDescending(r => r.Timestamp)
			.ToList();

		RevisionTimelineStack.Children.Clear();

		if (revisions.Count == 0)
		{
			RevisionLatestSummaryLabel.Text = "No edits logged yet.";
			RevisionTimelineStack.Children.Add(CreateEmptyLabel(
				"No revisions recorded yet."));
			return;
		}

		RevisionLatestSummaryLabel.Text = string.Join('\n',
			revisions.Take(3).Select(r =>
				$"• {(string.IsNullOrWhiteSpace(r.FieldChanged) ? "Record" : r.FieldChanged)} — {r.Timestamp:MMM dd} ({r.EditedBy})"));

		foreach (var revision in revisions)
		{
			var revisionCard = CreateRevisionCard(revision);
			RevisionTimelineStack.Children.Add(revisionCard);
		}
	}

	private static Label CreateEmptyLabel(string text) =>
		new()
		{
			Text = text,
			FontSize = 14,
			TextColor = Ui.NavyMutedText,
			HorizontalOptions = LayoutOptions.Center,
			HorizontalTextAlignment = TextAlignment.Center,
			LineBreakMode = LineBreakMode.WordWrap,
			Margin = new Thickness(12, 24, 12, 0)
		};

	private Frame CreateRevisionCard(RevisionLog revision)
	{
		return new Frame
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
					new Grid
					{
						ColumnDefinitions =
						{
							new ColumnDefinition { Width = GridLength.Star },
							new ColumnDefinition { Width = GridLength.Auto }
						},
						Children =
						{
							new VerticalStackLayout
							{
								Spacing = 2,
								Children =
								{
									new Label
									{
										Text = revision.EditedBy,
										FontSize = 13,
										FontAttributes = FontAttributes.Bold,
										TextColor = Ui.Navy
									},
									new Label
									{
										Text = revision.Role,
										FontSize = 11,
										TextColor = Ui.NavyMutedText
									}
								}
							},
							new Label
							{
								Text = FormatDateTime(revision.Timestamp),
								FontSize = 10,
								TextColor = Ui.NavyMutedText,
								VerticalOptions = LayoutOptions.Start
							}
						}
					},

					new BoxView
					{
						Color = Ui.NavyLine,
						Opacity = 0.45,
						HeightRequest = 1
					},

					new Frame
					{
						CornerRadius = 6,
						HasShadow = false,
						BorderColor = Colors.Transparent,
						Padding = new Thickness(8, 3),
						BackgroundColor = Ui.NavyWash,
						HorizontalOptions = LayoutOptions.Start,
						Content = new Label
						{
							Text = revision.FieldChanged,
							FontSize = 11,
							FontAttributes = FontAttributes.Bold,
							TextColor = Ui.Navy
						}
					},

					new VerticalStackLayout
					{
						Spacing = 6,
						Children =
						{
							new Frame
							{
								CornerRadius = 8,
								Padding = new Thickness(10, 8),
								BorderColor = Ui.Navy,
								BackgroundColor = Ui.NavyWash,
								HasShadow = false,
								Content = new VerticalStackLayout
								{
									Spacing = 2,
									Children =
									{
										new Label
										{
											Text = "BEFORE",
											FontSize = 9,
											TextColor = Ui.Navy,
											FontAttributes = FontAttributes.Bold
										},
										new Label
										{
											Text = revision.OldValue,
											FontSize = 12,
											TextColor = Ui.Navy,
											LineBreakMode = LineBreakMode.WordWrap
										}
									}
								}
							},

							new Frame
							{
								CornerRadius = 8,
								Padding = new Thickness(10, 8),
								BorderColor = Ui.NavyLine,
								BackgroundColor = Ui.White,
								HasShadow = false,
								Content = new VerticalStackLayout
								{
									Spacing = 2,
									Children =
									{
										new Label
										{
											Text = "AFTER",
											FontSize = 9,
											TextColor = Ui.Navy,
											FontAttributes = FontAttributes.Bold
										},
										new Label
										{
											Text = revision.NewValue,
											FontSize = 12,
											TextColor = Ui.Navy,
											LineBreakMode = LineBreakMode.WordWrap
										}
									}
								}
							}
						}
					}
				}
			}
		};
	}

	private static string FormatDateTime(DateTime dateTime)
	{
		var timeSpan = DateTime.Now - dateTime;

		if (timeSpan.TotalHours < 1)
		{
			return $"{(int)timeSpan.TotalMinutes}m ago";
		}

		if (timeSpan.TotalHours < 24)
		{
			return $"{(int)timeSpan.TotalHours}h ago";
		}

		if (timeSpan.TotalDays < 7)
		{
			return $"{(int)timeSpan.TotalDays}d ago";
		}

		if (timeSpan.TotalDays < 365)
		{
			return dateTime.ToString("MMM dd");
		}

		return dateTime.ToString("MMM dd, yyyy");
	}
}
