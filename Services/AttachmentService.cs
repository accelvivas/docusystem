using System.Text.Json;
using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// Talks to the Laravel attachment endpoints. The list endpoint is plain JSON; the
/// view/download/stream endpoints return either a redirect URL or a JSON envelope
/// like <c>{ "url": "https://..." }</c>, depending on the controller.
/// </summary>
public sealed class AttachmentService : IAttachmentService
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly IHttpClientFactory _httpClientFactory;

	public AttachmentService(IHttpClientFactory httpClientFactory)
	{
		_httpClientFactory = httpClientFactory;
	}

	public async Task<IReadOnlyList<ProposalAttachment>> GetAttachmentsAsync(int proposalId, CancellationToken cancellationToken = default)
	{
		try
		{
			var client = _httpClientFactory.CreateClient("LaravelApi");
			using var response = await client
				.GetAsync($"api/proposals/{proposalId}/attachments", cancellationToken)
				.ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				return [];
			}

			var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ParseAttachmentList(json);
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

	public Task<string?> GetViewUrlAsync(int attachmentId, CancellationToken cancellationToken = default) =>
		GetSignedUrlAsync($"api/attachments/{attachmentId}/view", cancellationToken);

	public Task<string?> GetDownloadUrlAsync(int attachmentId, CancellationToken cancellationToken = default) =>
		GetSignedUrlAsync($"api/attachments/{attachmentId}/download", cancellationToken);

	public Task<string?> GetStreamUrlAsync(int attachmentId, CancellationToken cancellationToken = default) =>
		GetSignedUrlAsync($"api/attachments/{attachmentId}/stream", cancellationToken);

	private async Task<string?> GetSignedUrlAsync(string relativePath, CancellationToken cancellationToken)
	{
		try
		{
			// "NoRedirect" client keeps the 3xx so we can lift the signed URL out of the Location
			// header. With auto-redirect on, HttpClient would silently follow it and we'd end up
			// downloading the file body instead of returning a viewer-friendly URL.
			var client = _httpClientFactory.CreateClient("LaravelApiNoRedirect");
			using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
			using var response = await client
				.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);

			// Some controllers redirect straight to the signed URL.
			if (response.Headers.Location is not null)
			{
				return response.Headers.Location.IsAbsoluteUri
					? response.Headers.Location.ToString()
					: new Uri(client.BaseAddress!, response.Headers.Location).ToString();
			}

			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return ExtractUrl(body);
		}
		catch (HttpRequestException)
		{
			return null;
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static IReadOnlyList<ProposalAttachment> ParseAttachmentList(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return [];
		}

		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		if (root.ValueKind == JsonValueKind.Array)
		{
			return root.Deserialize<List<ProposalAttachment>>(JsonOptions) ?? [];
		}

		if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
		{
			return data.Deserialize<List<ProposalAttachment>>(JsonOptions) ?? [];
		}

		return [];
	}

	private static string? ExtractUrl(string body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return null;
		}

		var trimmed = body.Trim();

		// Plain string body, e.g. "https://...".
		if (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
		{
			return trimmed.Trim('"');
		}

		// Plain URL line.
		if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
		{
			return trimmed;
		}

		try
		{
			using var doc = JsonDocument.Parse(trimmed);
			var root = doc.RootElement;
			foreach (var key in new[] { "url", "stream_url", "view_url", "download_url", "location" })
			{
				if (root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
				{
					return prop.GetString();
				}
			}

			if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
			{
				foreach (var key in new[] { "url", "stream_url", "view_url", "download_url", "location" })
				{
					if (data.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
					{
						return prop.GetString();
					}
				}
			}
		}
		catch (JsonException)
		{
			return null;
		}

		return null;
	}
}
