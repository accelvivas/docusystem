using System.Net.Http.Json;
using System.Text.Json;
using docusystem.Models;

namespace docusystem.Services;

/// <summary>Notifications from the Laravel API.</summary>
public sealed class NotificationService : INotificationService
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly IHttpClientFactory _httpClientFactory;

	public NotificationService(IHttpClientFactory httpClientFactory)
	{
		_httpClientFactory = httpClientFactory;
	}

	public async Task<IReadOnlyList<NotificationItem>> GetNotificationsAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var response = await client.GetAsync("api/notifications", cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return [];
			}

			var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ParseNotificationList(json);
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

	public async Task MarkAsReadAsync(int notificationId, CancellationToken cancellationToken = default)
	{
		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var request = new HttpRequestMessage(HttpMethod.Patch, $"api/notifications/{notificationId}/read");
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
			using var request = new HttpRequestMessage(HttpMethod.Patch, "api/notifications/read-all");
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
			return root.Deserialize<List<NotificationItem>>(JsonOptions) ?? [];
		}

		if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
		{
			return data.Deserialize<List<NotificationItem>>(JsonOptions) ?? [];
		}

		return [];
	}
}
