using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using docusystem.Models;

namespace docusystem.Services;

/// <summary>Notifications from the Laravel API (scoped server-side to the signed-in user / approver).</summary>
public sealed class NotificationService : INotificationService
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
	};

	/// <summary>
	/// Lenient parsing so one odd field does not drop the entire list (common with Laravel Resource variance).
	/// </summary>
	private static readonly JsonSerializerOptions NotificationJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		NumberHandling = JsonNumberHandling.AllowReadingFromString,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	private readonly IHttpClientFactory _httpClientFactory;

	private static readonly string[] CandidateGetPaths =
	[
		"api/notifications",
		"api/user/notifications",
		"api/me/notifications",
		"api/v1/notifications",
		"api/v1/user/notifications",
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
#if DEBUG
					System.Diagnostics.Debug.WriteLine(
						$"[notifications] GET {CandidateGetPaths[i]} → {(int)response.StatusCode}");
#endif
					if ((int)response.StatusCode is 401)
					{
						return [];
					}

					continue;
				}

				var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
				LastListUnreadCountFromMeta = TryReadMetaUnreadCount(json);
				var list = ParseNotificationList(json);
#if DEBUG
				System.Diagnostics.Debug.WriteLine(
					$"[notifications] GET {CandidateGetPaths[i]} → {list.Count} item(s)");
#endif
				return list;
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

	public async Task MarkAsReadAsync(string notificationRouteId, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(notificationRouteId))
		{
			return;
		}

		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			var encoded = Uri.EscapeDataString(notificationRouteId.Trim());
			using var request = new HttpRequestMessage(HttpMethod.Patch, $"api/notifications/{encoded}/read")
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
		if (string.IsNullOrWhiteSpace(json))
		{
			return [];
		}

		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;

		var primary = ParseNotificationListFromRoot(root);
		if (primary.Count > 0)
		{
			return primary;
		}

		if (TryFindNotificationArrayRecursive(root, 0, out var nested))
		{
			return DeserializeNotificationArray(nested);
		}

		return [];
	}

	private static IReadOnlyList<NotificationItem> ParseNotificationListFromRoot(JsonElement root)
	{
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

	private static bool TryFindNotificationArrayRecursive(JsonElement el, int depth, out JsonElement array)
	{
		const int maxDepth = 10;
		array = default;
		if (depth > maxDepth)
		{
			return false;
		}

		if (el.ValueKind == JsonValueKind.Array && LooksLikeNotificationArray(el))
		{
			array = el;
			return true;
		}

		if (el.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		foreach (var prop in el.EnumerateObject())
		{
			if (TryFindNotificationArrayRecursive(prop.Value, depth + 1, out array))
			{
				return true;
			}
		}

		return false;
	}

	private static bool LooksLikeNotificationArray(JsonElement arr)
	{
		if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
		{
			return false;
		}

		var first = arr[0];
		if (first.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		return first.TryGetProperty("title", out _) ||
		       first.TryGetProperty("message", out _) ||
		       first.TryGetProperty("body", out _) ||
		       first.TryGetProperty("notification_type", out _) ||
		       first.TryGetProperty("type", out _) ||
		       first.TryGetProperty("data", out _);
	}

	private static List<NotificationItem> DeserializeNotificationArray(JsonElement arr)
	{
		var list = new List<NotificationItem>();
		if (arr.ValueKind != JsonValueKind.Array)
		{
			return list;
		}

		foreach (var el in arr.EnumerateArray())
		{
			if (el.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			NotificationItem? item = null;
			try
			{
				item = el.Deserialize<NotificationItem>(NotificationJsonOptions);
			}
			catch (JsonException)
			{
				item = null;
			}

			item ??= TryMapNotificationLoose(el);
			var resolved = item;
			if (resolved is null)
			{
				continue;
			}

			HydrateStringIdFromElement(resolved, el);

			if (IsNoiseNotificationRow(resolved))
			{
				continue;
			}

			list.Add(resolved);
		}

		return list;
	}

	private static bool IsNoiseNotificationRow(NotificationItem? item)
	{
		if (item is null)
		{
			return true;
		}

		return item.Id <= 0 &&
		       string.IsNullOrWhiteSpace(item.StringId) &&
		       string.IsNullOrWhiteSpace(item.Title) &&
		       string.IsNullOrWhiteSpace(item.Message) &&
		       string.IsNullOrWhiteSpace(item.Body) &&
		       string.IsNullOrWhiteSpace(item.MessageBody) &&
		       string.IsNullOrWhiteSpace(item.Type) &&
		       string.IsNullOrWhiteSpace(item.NotificationType);
	}

	private static void HydrateStringIdFromElement(NotificationItem item, JsonElement el)
	{
		if (item.Id != 0 || !el.TryGetProperty("id", out var idEl))
		{
			return;
		}

		if (idEl.ValueKind != JsonValueKind.String)
		{
			return;
		}

		var s = idEl.GetString()?.Trim();
		if (string.IsNullOrEmpty(s))
		{
			return;
		}

		if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
		{
			item.Id = n;
			return;
		}

		item.StringId = s;
	}

	private static NotificationItem? TryMapNotificationLoose(JsonElement el)
	{
		if (el.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		var item = new NotificationItem();

		if (el.TryGetProperty("id", out var idEl))
		{
			if (idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out var nid))
			{
				item.Id = nid;
			}
			else if (idEl.ValueKind == JsonValueKind.String)
			{
				var sid = idEl.GetString()?.Trim();
				if (!string.IsNullOrEmpty(sid))
				{
					if (int.TryParse(sid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
					{
						item.Id = pid;
					}
					else
					{
						item.StringId = sid;
					}
				}
			}
		}

		item.Title = ReadString(el, "title", "subject") ?? string.Empty;
		item.Message = ReadString(el, "message", "body", "content") ?? string.Empty;
		item.Body = ReadString(el, "body");
		item.MessageBody = ReadString(el, "message_body");
		item.MessageTitle = ReadString(el, "message_title");
		item.Type = ReadString(el, "type") ?? string.Empty;
		item.NotificationType = ReadString(el, "notification_type");
		item.LinkUrl = ReadString(el, "link_url");
		item.LinkUrlCamel = ReadString(el, "linkUrl");

		if (el.TryGetProperty("proposal_id", out var pidEl) &&
		    pidEl.ValueKind == JsonValueKind.Number &&
		    pidEl.TryGetInt32(out var proposalId))
		{
			item.ProposalId = proposalId;
		}

		if (el.TryGetProperty("read_at", out var ra))
		{
			if (ra.ValueKind == JsonValueKind.String &&
			    DateTime.TryParse(ra.GetString(), CultureInfo.InvariantCulture,
				    DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var rd))
			{
				item.ReadAt = rd;
			}
			else if (ra.ValueKind == JsonValueKind.Null)
			{
				item.ReadAt = null;
			}
		}

		if (el.TryGetProperty("is_read", out var ir) && ir.ValueKind == JsonValueKind.True)
		{
			item.IsReadFlag = true;
		}

		if (el.TryGetProperty("created_at", out var ca))
		{
			if (ca.ValueKind == JsonValueKind.String &&
			    DateTime.TryParse(ca.GetString(), CultureInfo.InvariantCulture,
				    DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var cd))
			{
				item.CreatedAt = cd;
			}
		}

		return item;
	}

	private static string? ReadString(JsonElement obj, params string[] keys)
	{
		foreach (var key in keys)
		{
			if (!obj.TryGetProperty(key, out var p))
			{
				continue;
			}

			if (p.ValueKind == JsonValueKind.String)
			{
				return p.GetString();
			}

			if (p.ValueKind == JsonValueKind.Number)
			{
				return p.ToString();
			}
		}

		return null;
	}

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
