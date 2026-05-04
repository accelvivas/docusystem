using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using docusystem.Models;

namespace docusystem.Services;

/// <summary>Notifications from the Laravel API (scoped server-side to the signed-in user / approver).</summary>
public sealed class NotificationService : INotificationService
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly IHttpClientFactory _httpClientFactory;

	private static readonly string[] CandidateGetPaths =
	[
		"api/notifications",
		"api/user/notifications",
		"api/me/notifications"
	];

	public NotificationService(IHttpClientFactory httpClientFactory)
	{
		_httpClientFactory = httpClientFactory;
	}

	/// <inheritdoc />
	public int? LastListUnreadCountFromMeta { get; private set; }

	public async Task<IReadOnlyList<NotificationItem>> GetNotificationsAsync(CancellationToken cancellationToken = default)
	{
		LastListUnreadCountFromMeta = null;
		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");

			for (var i = 0; i < CandidateGetPaths.Length; i++)
			{
				using var response = await client.GetAsync(CandidateGetPaths[i], cancellationToken).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode)
				{
					if ((int)response.StatusCode is 401)
					{
						return [];
					}

					continue;
				}

				var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
				LastListUnreadCountFromMeta = TryReadMetaUnreadCount(json);
				return ParseNotificationList(json);
			}

			return [];
		}
		catch (HttpRequestException)
		{
			return [];
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return [];
		}
		catch (JsonException)
		{
			return [];
		}
	}

	public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var response = await client.GetAsync("api/notifications/unread-count", cancellationToken).ConfigureAwait(false);
			if (response.IsSuccessStatusCode)
			{
				var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
				var parsed = ParseUnreadCount(json);
				if (parsed >= 0)
				{
					return parsed;
				}
			}
		}
		catch (HttpRequestException)
		{
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
		}
		catch (JsonException)
		{
		}

		var list = await GetNotificationsAsync(cancellationToken).ConfigureAwait(false);
		return list.Count(n => !n.IsRead);
	}

	public async Task MarkAsReadAsync(int notificationId, CancellationToken cancellationToken = default)
	{
		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var request = new HttpRequestMessage(HttpMethod.Patch, $"api/notifications/{notificationId}/read")
			{
				Content = JsonContent.Create(new { }, options: JsonOptions)
			};
			await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
		}
		catch (HttpRequestException)
		{
		}
		catch (TaskCanceledException)
		{
		}
	}

	public async Task MarkAllAsReadAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var request = new HttpRequestMessage(HttpMethod.Patch, "api/notifications/read-all")
			{
				Content = JsonContent.Create(new { }, options: JsonOptions)
			};
			await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
		}
		catch (HttpRequestException)
		{
		}
		catch (TaskCanceledException)
		{
		}
	}

	private static IReadOnlyList<NotificationItem> ParseNotificationList(string json)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;

		if (root.ValueKind == JsonValueKind.Array)
		{
			return DeserializeNotificationArray(root);
		}

		if (root.ValueKind != JsonValueKind.Object)
		{
			return [];
		}

		if (TryGetNotificationArray(root, out var arr))
		{
			return DeserializeNotificationArray(arr);
		}

		if (root.TryGetProperty("data", out var dataEl))
		{
			if (dataEl.ValueKind == JsonValueKind.Array)
			{
				return DeserializeNotificationArray(dataEl);
			}

			if (dataEl.ValueKind == JsonValueKind.Object && TryGetNotificationArray(dataEl, out arr))
			{
				return DeserializeNotificationArray(arr);
			}
		}

		return [];
	}

	private static bool TryGetNotificationArray(JsonElement obj, out JsonElement array)
	{
		ReadOnlySpan<string> keys =
		[
			"data", "notifications", "items", "results", "rows", "payload"
		];

		for (var i = 0; i < keys.Length; i++)
		{
			if (obj.TryGetProperty(keys[i], out var el) && el.ValueKind == JsonValueKind.Array)
			{
				array = el;
				return true;
			}
		}

		array = default;
		return false;
	}

	private static List<NotificationItem> DeserializeNotificationArray(JsonElement arr)
	{
		return arr.Deserialize<List<NotificationItem>>(JsonOptions) ?? [];
	}

	/// <summary>Laravel <c>GET /api/notifications</c> returns <c>meta.unread_count</c> alongside <c>data</c>.</summary>
	private static int? TryReadMetaUnreadCount(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("meta", out var meta) ||
			    meta.ValueKind != JsonValueKind.Object)
			{
				return null;
			}

			if (meta.TryGetProperty("unread_count", out var u) && u.ValueKind == JsonValueKind.Number &&
			    u.TryGetInt32(out var n))
			{
				return n;
			}
		}
		catch (JsonException)
		{
		}

		return null;
	}

	private static int ParseUnreadCount(string json)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		if (root.ValueKind == JsonValueKind.Number && root.TryGetInt32(out var directCount))
		{
			return directCount;
		}

		if (root.ValueKind == JsonValueKind.Object)
		{
			ReadOnlySpan<string> keys = ["count", "unread_count", "unreadCount"];
			for (var i = 0; i < keys.Length; i++)
			{
				if (root.TryGetProperty(keys[i], out var c) &&
				    c.ValueKind == JsonValueKind.Number &&
				    c.TryGetInt32(out var n))
				{
					return n;
				}
			}

			if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
			{
				for (var i = 0; i < keys.Length; i++)
				{
					if (data.TryGetProperty(keys[i], out var c) &&
					    c.ValueKind == JsonValueKind.Number &&
					    c.TryGetInt32(out var n))
					{
						return n;
					}
				}
			}
		}

		return -1;
	}
}
