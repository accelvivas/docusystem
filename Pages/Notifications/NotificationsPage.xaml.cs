namespace docusystem.Pages.Notifications;

using System.Text.RegularExpressions;
using docusystem.Models;
using docusystem.Services;
using Ui = docusystem.UiBrand;

/// <summary>
/// In-app notifications for the signed-in user (approvers receive items scoped by Laravel).
/// </summary>
public partial class NotificationsPage : ContentPage
{
	private readonly INotificationService _notificationService;
	private readonly AppSessionService _session;
	private readonly IProposalService _proposalService;
	private List<NotificationItem> allNotifications = [];
	private string currentFilter = "All";

	public NotificationsPage(
		INotificationService notificationService,
		AppSessionService session,
		IProposalService proposalService)
	{
		InitializeComponent();
		_notificationService = notificationService;
		_session = session;
		_proposalService = proposalService;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		try
		{
			SetLoadingState(true);
			ErrorStateLabel.IsVisible = false;
			await LoadNotificationsAsync();
			SetLoadingState(false);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(ex);
			SetLoadingState(false);
			ErrorStateLabel.Text = "Could not load notifications. Pull down to retry.";
			ErrorStateLabel.IsVisible = true;
		}
	}

	private async void OnNotificationsRefreshing(object? sender, EventArgs e)
	{
		try
		{
			ErrorStateLabel.IsVisible = false;
			await LoadNotificationsAsync();
		}
		finally
		{
			NotificationsRefresh.IsRefreshing = false;
		}
	}

	private async Task LoadNotificationsAsync()
	{
		var items = await _notificationService.GetNotificationsAsync();
		allNotifications = items.OrderByDescending(n => n.DateCreated).ToList();
		var unread = allNotifications.Count(n => !n.IsRead);
		UnreadCountLabel.Text = unread.ToString();
		MarkAllReadBtn.IsVisible = unread > 0;
		ApplyFilter();
	}

	private void DisplayNotifications(List<NotificationItem> notificationsToDisplay)
	{
		NotificationsStack.Children.Clear();

		if (notificationsToDisplay.Count == 0)
		{
			NotificationsStack.Children.Add(
				new Label
				{
					Text = "No notifications yet.\n\nWhen proposals need your review or when there are revision updates, they will appear here.\n\nPull down to refresh.",
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

		foreach (var notification in notificationsToDisplay)
		{
			var notificationCard = CreateNotificationCard(notification);
			NotificationsStack.Children.Add(notificationCard);
		}
	}

	private Border CreateNotificationCard(NotificationItem notification)
	{
		var isUnread = !notification.IsRead;
		var badgeColor = isUnread ? Ui.Navy : Ui.NavyWash;
		var badgeText = isUnread ? "Unread" : "Read";
		var cardBg = isUnread ? Ui.NavyWash : Ui.White;
		var borderColor = isUnread ? Ui.Navy : Ui.NavyLine;

		var badge = new Border
		{
			BackgroundColor = badgeColor,
			Stroke = Colors.Transparent,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
			StrokeThickness = 0,
			Padding = new Thickness(7, 3),
			VerticalOptions = LayoutOptions.Start,
			Content = new Label
			{
				Text = badgeText,
				FontSize = 10,
				FontAttributes = FontAttributes.Bold,
				TextColor = isUnread ? Colors.White : Ui.Navy
			}
		};
		Grid.SetColumn(badge, 2);

		var iconBadge = new Border
		{
			BackgroundColor = isUnread ? Ui.Navy : Ui.NavyWash,
			Stroke = Colors.Transparent,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
			Padding = new Thickness(8, 4),
			Content = new Label
			{
				Text = notification.IconGlyph,
				FontSize = 14,
				TextColor = isUnread ? Colors.White : Ui.Navy
			}
		};
		var textStack = new VerticalStackLayout
		{
			Spacing = 3,
			Children =
			{
				new Label
				{
					Text = notification.DisplayTitle,
					FontSize = 14,
					FontAttributes = FontAttributes.Bold,
					TextColor = Ui.Navy
				},
				new Label
				{
					Text = string.IsNullOrWhiteSpace(notification.DisplayMessage)
						? "(No message)"
						: notification.DisplayMessage,
					FontSize = 12,
					TextColor = Ui.NavyMutedText,
					LineBreakMode = LineBreakMode.WordWrap
				},
				new Label
				{
					Text = $"Activity Proposal: {notification.DisplayProposalTitle}",
					FontSize = 11,
					TextColor = Ui.Navy
				},
				new Label
				{
					Text = $"Organization: {notification.DisplayOrganizationName}",
					FontSize = 11,
					TextColor = Ui.NavyMutedText
				}
			}
		};
		Grid.SetColumn(iconBadge, 0);
		Grid.SetColumn(textStack, 1);

		var cardBorder = new Border
		{
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
			Stroke = borderColor,
			StrokeThickness = 1,
			Padding = 14,
			BackgroundColor = cardBg,
			Content = new VerticalStackLayout
			{
				Spacing = 8,
				Children =
				{
					new Grid
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
							iconBadge,
							textStack,
							badge
						}
					},
					BuildMetaLine(notification),
					new Label
					{
						Text = FormatDateTime(notification.DateCreated),
						FontSize = 11,
						TextColor = Ui.NavyMutedText
					}
				}
			}
		};
		var tap = new TapGestureRecognizer();
		tap.Tapped += async (_, _) => await OnNotificationTappedAsync(notification).ConfigureAwait(true);
		cardBorder.GestureRecognizers.Add(tap);

		return cardBorder;
	}

	private static View BuildMetaLine(NotificationItem notification)
	{
		var stage = notification.DisplayStage;
		var status = notification.DisplayStatus;
		var actor = string.IsNullOrWhiteSpace(notification.ActorName) ? null : notification.ActorName;

		var parts = new List<string>();
		if (!string.IsNullOrWhiteSpace(stage))
		{
			parts.Add($"Stage: {stage}");
		}
		if (!string.IsNullOrWhiteSpace(status))
		{
			parts.Add($"Status: {status}");
		}
		if (!string.IsNullOrWhiteSpace(actor))
		{
			parts.Add($"By: {actor}");
		}

		return new Label
		{
			Text = parts.Count == 0 ? "Tap to open details" : string.Join(" • ", parts),
			FontSize = 11,
			TextColor = Ui.NavyMutedText,
			LineBreakMode = LineBreakMode.WordWrap
		};
	}

	private async Task OnNotificationTappedAsync(NotificationItem notification)
	{
		if (!TryResolveProposalId(notification, out var proposalId))
		{
			await DisplayAlertAsync(
				"No proposal link",
				"This notification does not include a proposal you can open from the app.",
				"OK");
			return;
		}

		try
		{
			var proposal = await _proposalService.GetProposalByIdAsync(proposalId).ConfigureAwait(true);
			if (proposal is null)
			{
				await DisplayAlertAsync(
					"Proposal unavailable",
					"This proposal could not be loaded. You may not have access or it may have been removed.",
					"OK");
				return;
			}

			_session.SetSelectedProposal(proposal);

			if (!notification.IsRead && notification.Id > 0)
			{
				_ = _notificationService.MarkAsReadAsync(notification.Id);
				notification.ReadAt = DateTime.UtcNow;
				notification.IsReadFlag = true;
				ApplyFilter();
				UnreadCountLabel.Text = allNotifications.Count(n => !n.IsRead).ToString();
				MarkAllReadBtn.IsVisible = allNotifications.Any(n => !n.IsRead);
			}

			var route = ResolveTargetRoute(notification);
			await Shell.Current.GoToAsync(route).ConfigureAwait(true);
		}
		catch (Exception)
		{
			await DisplayAlertAsync("Something went wrong", "Could not open this proposal. Try again.", "OK");
		}
	}

	private static string ResolveTargetRoute(NotificationItem notification)
	{
		var target = (notification.ScreenTarget ?? string.Empty).Trim().ToLowerInvariant();
		if (target.Contains("revision"))
		{
			return "//revisionhistory";
		}
		if (target.Contains("approval"))
		{
			return "//pendingapprovals";
		}

		return notification.TypeKey switch
		{
			"proposal_returned_for_revision" => "//revisionhistory",
			"revision_history_updated" => "//revisionhistory",
			"new_pending_proposal" => "proposaldetails",
			"proposal_resubmitted" => "proposaldetails",
			"approval_reminder" => "//pendingapprovals",
			_ => "proposaldetails"
		};
	}

	private static bool TryResolveProposalId(NotificationItem n, out int id)
	{
		id = 0;
		if (n.ProposalId > 0)
		{
			id = n.ProposalId;
			return true;
		}

		var link = n.ResolvedLinkUrl;
		if (string.IsNullOrWhiteSpace(link))
		{
			return false;
		}

		var m = Regex.Match(link, @"proposals?\/(\d+)", RegexOptions.IgnoreCase);
		if (m.Success && int.TryParse(m.Groups[1].Value, out id))
		{
			return true;
		}

		m = Regex.Match(link, @"[?&]proposal[_-]?id=(\d+)", RegexOptions.IgnoreCase);
		if (m.Success && int.TryParse(m.Groups[1].Value, out id))
		{
			return true;
		}

		return false;
	}

	private static bool MatchesTypeFilter(NotificationItem n, string filterKey)
	{
		var t = (n.Type ?? string.Empty).Trim();
		var key = n.TypeKey;
		return filterKey switch
		{
			"Pending" => TypeMatchesAny(key, t, "new_pending_proposal", "approval_reminder", "pending_approval", "proposal_resubmitted"),
			"Returned" => TypeMatchesAny(key, t, "proposal_returned_for_revision", "revision_required", "revision_update"),
			"Approved" => TypeMatchesAny(key, t, "proposal_approved", "approved", "final_approval", "fully_approved"),
			"Rejected" => TypeMatchesAny(key, t, "proposal_rejected", "rejected"),
			"StatusUpdates" => TypeMatchesAny(key, t, "status_updated", "revision_history_updated", "approval_update"),
			_ => true
		};
	}

	private static bool TypeMatchesAny(string actual, params string[] candidates)
	{
		for (var i = 0; i < candidates.Length; i++)
		{
			if (string.Equals(actual, candidates[i], StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private void SetLoadingState(bool loading)
	{
		LoadingIndicator.IsVisible = loading;
		LoadingIndicator.IsRunning = loading;
	}

	private void OnFilterClicked(object? sender, EventArgs e)
	{
		if (sender is Button button)
		{
			currentFilter = button.ClassId;
			UpdateFilterUI();
			ApplyFilter();
		}
	}

	private void UpdateFilterUI()
	{
		ResetFilterButtonStyles();

		var activeButton = currentFilter switch
		{
			"All" => FilterAllBtn,
			"Pending" => FilterApprovalBtn,
			"Returned" => FilterRevisionBtn,
			"Approved" => FilterApprovedBtn,
			"Rejected" => FilterRejectedBtn,
			"StatusUpdates" => FilterStatusBtn,
			_ => FilterAllBtn
		};

		activeButton.BackgroundColor = Ui.Navy;
		activeButton.TextColor = Ui.White;
	}

	private void ResetFilterButtonStyles()
	{
		FilterAllBtn.BackgroundColor = Ui.NavyWash;
		FilterAllBtn.TextColor = Ui.Navy;

		FilterApprovalBtn.BackgroundColor = Ui.NavyWash;
		FilterApprovalBtn.TextColor = Ui.Navy;

		FilterRevisionBtn.BackgroundColor = Ui.NavyWash;
		FilterRevisionBtn.TextColor = Ui.Navy;

		FilterApprovedBtn.BackgroundColor = Ui.NavyWash;
		FilterApprovedBtn.TextColor = Ui.Navy;

		FilterRejectedBtn.BackgroundColor = Ui.NavyWash;
		FilterRejectedBtn.TextColor = Ui.Navy;

		FilterStatusBtn.BackgroundColor = Ui.NavyWash;
		FilterStatusBtn.TextColor = Ui.Navy;
	}

	private void ApplyFilter()
	{
		var filteredNotifications = currentFilter switch
		{
			"Pending" => allNotifications.Where(n => MatchesTypeFilter(n, "Pending")).ToList(),
			"Returned" => allNotifications.Where(n => MatchesTypeFilter(n, "Returned")).ToList(),
			"Approved" => allNotifications.Where(n => MatchesTypeFilter(n, "Approved")).ToList(),
			"Rejected" => allNotifications.Where(n => MatchesTypeFilter(n, "Rejected")).ToList(),
			"StatusUpdates" => allNotifications.Where(n => MatchesTypeFilter(n, "StatusUpdates")).ToList(),
			_ => allNotifications
		};

		DisplayNotifications(filteredNotifications);
	}

	private async void OnMarkAllReadClicked(object? sender, EventArgs e)
	{
		try
		{
			await _notificationService.MarkAllAsReadAsync();
			for (var i = 0; i < allNotifications.Count; i++)
			{
				allNotifications[i].ReadAt = DateTime.UtcNow;
				allNotifications[i].IsReadFlag = true;
			}

			UnreadCountLabel.Text = "0";
			MarkAllReadBtn.IsVisible = false;
			ApplyFilter();
		}
		catch
		{
			await DisplayAlertAsync("Unable to mark all as read", "Please try again.", "OK");
		}
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

		return dateTime.ToString("MMM dd, yyyy");
	}
}
