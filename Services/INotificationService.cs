using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// In-app notifications from Laravel.
/// </summary>
public interface INotificationService
{
	/// <summary>
	/// When the last <see cref="GetNotificationsAsync"/> response included <c>meta.unread_count</c>
	/// (as in the Laravel <c>NotificationController@index</c>), mirrors that value; otherwise <see langword="null"/>.
	/// </summary>
	int? LastListUnreadCountFromMeta { get; }

	/// <summary>GET notifications (tries <c>api/notifications</c> and common alternates). Laravel scopes to the authenticated user / approver.</summary>
	Task<IReadOnlyList<NotificationItem>> GetNotificationsAsync(CancellationToken cancellationToken = default);

	/// <summary>PATCH /api/notifications/{id}/read — marks a single notification as read (numeric id or UUID string).</summary>
	Task MarkAsReadAsync(string notificationRouteId, CancellationToken cancellationToken = default);

	/// <summary>PATCH /api/notifications/read-all — marks every notification for the user as read.</summary>
	Task MarkAllAsReadAsync(CancellationToken cancellationToken = default);

	/// <summary>GET /api/notifications/unread-count (or fallback from list).</summary>
	Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);
}
