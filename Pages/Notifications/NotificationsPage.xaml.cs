namespace docusystem.Pages.Notifications;

using docusystem.Models;
using docusystem.Services;
using Ui = docusystem.UiBrand;

/// <summary>
/// Notifications list — data from <see cref="INotificationService.GetNotificationsAsync"/> (TODO: Laravel).
/// </summary>
public partial class NotificationsPage : ContentPage
{
	private readonly INotificationService _notificationService;
	private List<NotificationItem> allNotifications = [];
	private string currentFilter = "All";

	public NotificationsPage(INotificationService notificationService)
	{
		InitializeComponent();
		_notificationService = notificationService;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await LoadNotificationsAsync();
	}

	private async void OnNotificationsRefreshing(object? sender, EventArgs e)
	{
		try
		{
			await LoadNotificationsAsync();
		}
		finally
		{
			NotificationsRefresh.IsRefreshing = false;
		}
	}

	private async Task LoadNotificationsAsync()
	{
		// TODO: GET /api/notifications — Laravel scopes to authenticated user
		var items = await _notificationService.GetNotificationsAsync();
		allNotifications = items.OrderByDescending(n => n.DateCreated).ToList();
		DisplayNotifications(allNotifications);
	}

	private void DisplayNotifications(List<NotificationItem> notificationsToDisplay)
	{
		NotificationsStack.Children.Clear();

		if (notificationsToDisplay.Count == 0)
		{
			NotificationsStack.Children.Add(
				new Label
				{
					Text = "You are all caught up\n\nThere are no notifications to show. When the server sends alerts about approvals or revisions, they will appear here.\n\nPull down to refresh.",
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

	private Frame CreateNotificationCard(NotificationItem notification)
	{
		var isUnread = !notification.IsRead;
		var badgeColor = isUnread ? Ui.Navy : Ui.NavyWash;
		var badgeText = isUnread ? "Unread" : "Read";
		var cardBg = isUnread ? Ui.NavyWash : Ui.White;
		var borderColor = isUnread ? Ui.Navy : Ui.NavyLine;

		return new Frame
		{
			CornerRadius = 14,
			BorderColor = borderColor,
			HasShadow = false,
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
							new ColumnDefinition { Width = GridLength.Star },
							new ColumnDefinition { Width = GridLength.Auto }
						},
						Children =
						{
							new VerticalStackLayout
							{
								Spacing = 3,
								Children =
								{
									new Label
									{
										Text = notification.Title,
										FontSize = 14,
										FontAttributes = FontAttributes.Bold,
										TextColor = Ui.Navy
									},
									new Label
									{
										Text = notification.Message,
										FontSize = 12,
										TextColor = Ui.NavyMutedText,
										LineBreakMode = LineBreakMode.WordWrap
									}
								}
							},
							new Frame
							{
								CornerRadius = 6,
								HasShadow = false,
								BorderColor = Colors.Transparent,
								Padding = new Thickness(7, 3),
								BackgroundColor = badgeColor,
								VerticalOptions = LayoutOptions.Start,
								Content = new Label
								{
									Text = badgeText,
									FontSize = 10,
									FontAttributes = FontAttributes.Bold,
									TextColor = isUnread ? Colors.White : Ui.Navy
								}
							}
						}
					},
					new Label
					{
						Text = FormatDateTime(notification.DateCreated),
						FontSize = 11,
						TextColor = Ui.NavyMutedText
					}
				}
			}
		};
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
			"Unread" => FilterUnreadBtn,
			"ApprovalUpdate" => FilterApprovalBtn,
			"RevisionUpdate" => FilterRevisionBtn,
			"FinalApproval" => FilterFinalBtn,
			_ => FilterAllBtn
		};

		activeButton.BackgroundColor = Ui.Navy;
		activeButton.TextColor = Ui.White;
	}

	private void ResetFilterButtonStyles()
	{
		FilterAllBtn.BackgroundColor = Ui.NavyWash;
		FilterAllBtn.TextColor = Ui.Navy;

		FilterUnreadBtn.BackgroundColor = Ui.NavyWash;
		FilterUnreadBtn.TextColor = Ui.Navy;

		FilterApprovalBtn.BackgroundColor = Ui.NavyWash;
		FilterApprovalBtn.TextColor = Ui.Navy;

		FilterRevisionBtn.BackgroundColor = Ui.NavyWash;
		FilterRevisionBtn.TextColor = Ui.Navy;

		FilterFinalBtn.BackgroundColor = Ui.NavyWash;
		FilterFinalBtn.TextColor = Ui.Navy;
	}

	private void ApplyFilter()
	{
		var filteredNotifications = currentFilter switch
		{
			"Unread" => allNotifications.Where(n => !n.IsRead).ToList(),
			"ApprovalUpdate" => allNotifications.Where(n => n.Type == "ApprovalUpdate").ToList(),
			"RevisionUpdate" => allNotifications.Where(n => n.Type == "RevisionUpdate").ToList(),
			"FinalApproval" => allNotifications.Where(n => n.Type == "FinalApproval").ToList(),
			_ => allNotifications
		};

		DisplayNotifications(filteredNotifications);
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
