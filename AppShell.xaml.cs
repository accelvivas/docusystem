using docusystem.Pages.Approvals;
using docusystem.Pages.Login;
using docusystem.Services;

namespace docusystem;

public partial class AppShell : Shell
{
	private const string NotificationsToolbarClassId = "shell_notifications_bell";

	private readonly AppSessionService _session;
	private readonly SessionPersistenceService _persistence;
	private readonly IAuthService _authService;
	private readonly INotificationService _notificationService;
	private bool _sessionRestoreStarted;

	public AppShell(
		AppSessionService session,
		SessionPersistenceService persistence,
		IAuthService authService,
		INotificationService notificationService)
	{
		_session = session;
		_persistence = persistence;
		_authService = authService;
		_notificationService = notificationService;
		InitializeComponent();
		// Contextual workflow pages (not flyout items): opened from Pending Approvals / Proposal Details.
		Routing.RegisterRoute("proposaldetails", typeof(ProposalDetailsPage));
		Navigating += OnShellNavigating;
		Navigated += OnShellNavigated;
		Loaded += OnShellLoaded;
		SetAuthenticatedState(false);
	}

	private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(SyncNotificationsToolbarItem);
	}

	private async void SyncNotificationsToolbarItem()
	{
		if (Current?.CurrentPage is not ContentPage page)
		{
			return;
		}

		RemoveNotificationsToolbarItems(page);

		if (_session.CurrentUser is null)
		{
			return;
		}

		if (page is LoginPage)
		{
			return;
		}

		if (!Shell.GetNavBarIsVisible(page))
		{
			return;
		}

		var item = new ToolbarItem
		{
			ClassId = NotificationsToolbarClassId,
			Order = ToolbarItemOrder.Primary,
			Priority = 100,
			Text = string.Empty,
			IconImageSource = ImageSource.FromFile("bell.svg")
		};
		item.Clicked += OnNotificationsToolbarClicked;
		page.ToolbarItems.Add(item);

		try
		{
			var unread = await _notificationService.GetUnreadCountAsync().ConfigureAwait(true);
			item.Text = unread > 0 ? unread.ToString() : string.Empty;
		}
		catch
		{
			item.Text = string.Empty;
		}
	}

	private static void RemoveNotificationsToolbarItems(ContentPage page)
	{
		for (var i = page.ToolbarItems.Count - 1; i >= 0; i--)
		{
			if (page.ToolbarItems[i].ClassId == NotificationsToolbarClassId)
			{
				page.ToolbarItems.RemoveAt(i);
			}
		}
	}

	private static async void OnNotificationsToolbarClicked(object? sender, EventArgs e)
	{
		try
		{
			await Shell.Current.GoToAsync("//notifications");
		}
		catch
		{
			// Ignore navigation races (e.g. during shell transitions).
		}
	}

	private void OnShellLoaded(object? sender, EventArgs e)
	{
		if (_sessionRestoreStarted)
		{
			return;
		}

		_sessionRestoreStarted = true;
		_ = RestoreSessionAsync();
	}

	private async Task RestoreSessionAsync()
	{
		try
		{
			await _persistence.TryRestoreAsync(_session, this).ConfigureAwait(false);
		}
		catch
		{
			// Ignore restore failures; user stays on login.
		}
	}

	private void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
	{
		if (_session.CurrentUser is not null)
		{
			return;
		}

		var dest = e.Target?.Location?.ToString() ?? string.Empty;
		if (dest.Contains("login", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		if (IsProtectedRoute(dest))
		{
			e.Cancel();
			MainThread.BeginInvokeOnMainThread(async () =>
			{
				try
				{
					await Current.GoToAsync("//login");
				}
				catch
				{
					// Avoid surfacing navigation races during shell initialization.
				}
			});
		}
	}

	private static bool IsProtectedRoute(string dest)
	{
		if (string.IsNullOrEmpty(dest))
		{
			return false;
		}

		ReadOnlySpan<string> routes =
		[
			"dashboard",
			"notifications",
			"pendingapprovals",
			"proposaldetails",
			"revisionhistory"
		];

		foreach (var r in routes)
		{
			if (dest.Contains(r, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private async void OnLogoutFromFlyoutClicked(object? sender, EventArgs e)
	{
		try
		{
			await _authService.LogoutAsync().ConfigureAwait(true);
			SetAuthenticatedState(false);
			await GoToAsync("//login").ConfigureAwait(true);
		}
		catch (Exception)
		{
			// Still try to return to sign-in; avoid crashing on navigation races
			_session.ClearSession();
			try
			{
				SetAuthenticatedState(false);
				await GoToAsync("//login").ConfigureAwait(true);
			}
			catch
			{
				// best-effort
			}
		}
	}

	public void SetAuthenticatedState(bool isAuthenticated)
	{
		FlyoutBehavior = isAuthenticated ? FlyoutBehavior.Flyout : FlyoutBehavior.Disabled;

		LoginShellContent.IsVisible = !isAuthenticated;
		DashboardShellContent.IsVisible = isAuthenticated;
		NotificationsShellContent.IsVisible = isAuthenticated;
		PendingApprovalsShellContent.IsVisible = isAuthenticated;
		RevisionHistoryShellContent.IsVisible = isAuthenticated;

		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (Current?.CurrentPage is ContentPage page)
			{
				RemoveNotificationsToolbarItems(page);
				if (isAuthenticated && page is not LoginPage && Shell.GetNavBarIsVisible(page))
				{
					SyncNotificationsToolbarItem();
				}
			}
		});
	}
}
