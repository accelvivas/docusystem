using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// In-app notifications from Laravel.
/// </summary>
public interface INotificationService
{
	/// <summary>TODO: GET /api/notifications — server scopes to authenticated user.</summary>
	Task<IReadOnlyList<NotificationItem>> GetNotificationsAsync(CancellationToken cancellationToken = default);

	/// <summary>TODO: PATCH /api/notifications/{id}/read</summary>
	Task MarkAsReadAsync(int notificationId, CancellationToken cancellationToken = default);
}
