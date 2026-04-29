using System.Text.Json.Serialization;

namespace docusystem.Models;

/// <summary>
/// Single file attached to a proposal. Lines up with what
/// <c>ProposalAttachmentController@index</c> returns:
/// <c>{ id, file_type, original_name, mime_type, file_size_kb, view_url, download_url }</c>.
/// Older contract fields (<c>file_name</c>, <c>size</c>, <c>category</c>, <c>uploaded_by</c>)
/// are kept for forward/back compatibility.
/// </summary>
public sealed class ProposalAttachment
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("proposal_id")]
	public int ProposalId { get; set; }

	/// <summary>API: <c>file_type</c> — logical category (e.g. <c>request_letter</c>, <c>resume</c>).</summary>
	[JsonPropertyName("file_type")]
	public string? FileType { get; set; }

	[JsonPropertyName("file_name")]
	public string? FileName { get; set; }

	[JsonPropertyName("original_name")]
	public string? OriginalName { get; set; }

	[JsonPropertyName("mime_type")]
	public string? MimeType { get; set; }

	/// <summary>API: <c>file_size_kb</c> — rounded size in KiB (preferred).</summary>
	[JsonPropertyName("file_size_kb")]
	public long? FileSizeKb { get; set; }

	/// <summary>Legacy field — bytes; kept for older endpoints.</summary>
	[JsonPropertyName("size")]
	public long? Size { get; set; }

	[JsonPropertyName("category")]
	public string? Category { get; set; }

	[JsonPropertyName("uploaded_by")]
	public string? UploadedBy { get; set; }

	[JsonPropertyName("created_at")]
	public DateTime? CreatedAt { get; set; }

	/// <summary>Optional pre-signed stream URL when the API embeds it in the list response.</summary>
	[JsonPropertyName("stream_url")]
	public string? StreamUrl { get; set; }

	/// <summary>API: <c>view_url</c> — used to fetch a signed URL for in-browser preview.</summary>
	[JsonPropertyName("view_url")]
	public string? ViewUrl { get; set; }

	/// <summary>API: <c>download_url</c> — used to fetch a signed URL for download.</summary>
	[JsonPropertyName("download_url")]
	public string? DownloadUrl { get; set; }

	/// <summary>Best-effort display name (falls back to <see cref="FileName"/>).</summary>
	[JsonIgnore]
	public string DisplayName =>
		!string.IsNullOrWhiteSpace(OriginalName) ? OriginalName!
		: !string.IsNullOrWhiteSpace(FileName) ? FileName!
		: $"Attachment #{Id}";

	/// <summary>Friendly size in bytes, derived from whichever field the API populated.</summary>
	[JsonIgnore]
	public long? SizeBytes =>
		Size ?? (FileSizeKb.HasValue ? FileSizeKb.Value * 1024L : null);
}
