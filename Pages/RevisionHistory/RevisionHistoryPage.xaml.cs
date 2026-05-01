namespace docusystem.Pages.RevisionHistory;

using docusystem.Models;
using docusystem.Services;
using Ui = docusystem.UiBrand;

/// <summary>
/// View-only proposal action history for approvers/signatories.
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
		try
		{
			SetLoadingState(true);
			RevisionErrorStateLabel.IsVisible = false;
			await LoadRevisionHistoryAsync();
			SetLoadingState(false);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(ex);
			SetLoadingState(false);
			RevisionErrorStateLabel.Text = "Could not load revision history right now. Please try again.";
			RevisionErrorStateLabel.IsVisible = true;
		}
	}

	private async Task LoadRevisionHistoryAsync()
	{
		var selectedProposal = _session.SelectedProposal;
		if (selectedProposal is null)
		{
			LatestRevisionSummaryBorder.IsVisible = false;
			RevisionContextHintLabel.Text =
				"Open a proposal from Pending Approvals, then view its history.";
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
			RevisionLatestSummaryLabel.Text = "No history available yet.";
			RevisionTimelineStack.Children.Add(CreateEmptyLabel(
				"No history available yet."));
			return;
		}

		RevisionLatestSummaryLabel.Text = string.Join('\n',
			revisions.Take(3).Select(r =>
				$"• {r.DisplayTitle} — {r.Timestamp:MMM dd} ({r.DisplayActor})"));

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

	private Border CreateRevisionCard(RevisionLog revision)
	{
		var header = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = GridLength.Star },
				new ColumnDefinition { Width = GridLength.Auto }
			},
			ColumnSpacing = 10,
			Children =
			{
				new Border
				{
					BackgroundColor = Ui.NavyWash,
					Stroke = Colors.Transparent,
					StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
					Padding = new Thickness(8, 4),
					VerticalOptions = LayoutOptions.Start,
					Content = new Label
					{
						Text = revision.ActionIcon,
						FontSize = 14,
						TextColor = Ui.Navy
					}
				},
				new VerticalStackLayout
				{
					Spacing = 3,
					Children =
					{
						new Label
						{
							Text = revision.DisplayTitle,
							FontSize = 14,
							FontAttributes = FontAttributes.Bold,
							TextColor = Ui.Navy
						},
						new Label
						{
							Text = $"Activity Proposal: {revision.DisplayProposalTitle}",
							FontSize = 11,
							TextColor = Ui.Navy
						},
						new Label
						{
							Text = $"Organization: {revision.DisplayOrganizationName}",
							FontSize = 11,
							TextColor = Ui.NavyMutedText
						}
					}
				}.Assign(gridColumn: 1),
				new Label
				{
					Text = FormatDateTime(revision.Timestamp),
					FontSize = 10,
					TextColor = Ui.NavyMutedText,
					VerticalOptions = LayoutOptions.Start
				}.Assign(gridColumn: 2)
			}
		};

		var actorStatusLine = new Label
		{
			Text = BuildActorStatusText(revision),
			FontSize = 11,
			TextColor = Ui.NavyMutedText,
			LineBreakMode = LineBreakMode.WordWrap
		};

		var affectedFieldsLabel = new Label
		{
			Text = string.IsNullOrWhiteSpace(revision.DisplayAffectedFields)
				? string.Empty
				: $"Affected fields: {revision.DisplayAffectedFields}",
			FontSize = 11,
			TextColor = Ui.NavyMutedText,
			LineBreakMode = LineBreakMode.WordWrap,
			IsVisible = !string.IsNullOrWhiteSpace(revision.DisplayAffectedFields)
		};

		var notePanel = new Border
		{
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
			Padding = new Thickness(10, 8),
			Stroke = Ui.NavyLine,
			BackgroundColor = Ui.White,
			StrokeThickness = 1,
			Content = new VerticalStackLayout
			{
				Spacing = 2,
				Children =
				{
					new Label
					{
						Text = "REMARK / NOTE",
						FontSize = 9,
						TextColor = Ui.Navy,
						FontAttributes = FontAttributes.Bold
					},
					new Label
					{
						Text = string.IsNullOrWhiteSpace(revision.DisplayRemark) ? "(no note provided)" : revision.DisplayRemark,
						FontSize = 12,
						TextColor = Ui.Navy,
						LineBreakMode = LineBreakMode.WordWrap
					}
				}
			},
			IsVisible = !string.IsNullOrWhiteSpace(revision.DisplayRemark)
		};

		return new Border
		{
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
			Stroke = Ui.NavyLine,
			StrokeThickness = 1,
			Padding = 14,
			BackgroundColor = Ui.White,
			Content = new VerticalStackLayout
			{
				Spacing = 10,
				Children =
				{
					header,
					new BoxView
					{
						Color = Ui.NavyLine,
						Opacity = 0.45,
						HeightRequest = 1
					},
					actorStatusLine,
					affectedFieldsLabel,
					notePanel
				}
			}
		};
	}

	private void SetLoadingState(bool loading)
	{
		RevisionLoadingIndicator.IsVisible = loading;
		RevisionLoadingIndicator.IsRunning = loading;
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

	private static string BuildActorStatusText(RevisionLog revision)
	{
		var parts = new List<string>();
		if (!string.IsNullOrWhiteSpace(revision.DisplayActor))
		{
			parts.Add($"By: {revision.DisplayActor}");
		}
		if (!string.IsNullOrWhiteSpace(revision.DisplayActorRole))
		{
			parts.Add($"Role/Stage: {revision.DisplayActorRole}");
		}
		if (!string.IsNullOrWhiteSpace(revision.DisplayStatusAfterAction))
		{
			parts.Add($"Status after action: {revision.DisplayStatusAfterAction}");
		}

		return parts.Count == 0 ? "History entry recorded." : string.Join(" • ", parts);
	}
}

internal static class RevisionHistoryUiExtensions
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
