using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// In-app notifications from Laravel.
/// </summary>
public interface INotificationService
{
	/// <summary>GET notifications (tries <c>api/notifications</c> and common alternates). Laravel scopes to the authenticated user / approver.</summary>
	Task<IReadOnlyList<NotificationItem>> GetNotificationsAsync(CancellationToken cancellationToken = default);

	/// <summary>PATCH /api/notifications/{id}/read — marks a single notification as read.</summary>
	Task MarkAsReadAsync(int notificationId, CancellationToken cancellationToken = default);

	/// <summary>PATCH /api/notifications/read-all — marks every notification for the user as read.</summary>
	Task MarkAllAsReadAsync(CancellationToken cancellationToken = default);

	/// <summary>GET /api/notifications/unread-count (or fallback from list).</summary>
	Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);
}
