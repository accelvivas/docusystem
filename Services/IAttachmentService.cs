using docusystem.Models;

namespace docusystem.Services;

/// <summary>
/// File attachments tied to a proposal. Maps directly to the mobile API:
/// <list type="bullet">
///   <item><c>GET /api/proposals/{id}/attachments</c> → <see cref="GetAttachmentsAsync"/></item>
///   <item><c>GET /api/attachments/{id}/view</c> → <see cref="GetViewUrlAsync"/></item>
///   <item><c>GET /api/attachments/{id}/download</c> → <see cref="GetDownloadUrlAsync"/></item>
///   <item><c>GET /api/attachments/{id}/stream</c> (signed) → <see cref="GetStreamUrlAsync"/></item>
/// </list>
/// </summary>
public interface IAttachmentService
{
	/// <summary>List every attachment that belongs to the given proposal.</summary>
	Task<IReadOnlyList<ProposalAttachment>> GetAttachmentsAsync(int proposalId, CancellationToken cancellationToken = default);

	/// <summary>Returns a (typically signed) URL the OS file viewer can open in-browser.</summary>
	Task<string?> GetViewUrlAsync(int attachmentId, CancellationToken cancellationToken = default);

	/// <summary>Returns a (typically signed) URL that triggers a download.</summary>
	Task<string?> GetDownloadUrlAsync(int attachmentId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Builds the public, signed <c>/api/attachments/{id}/stream</c> URL the controller hands out.
	/// Use this when the backend returns a pre-signed link inside <see cref="ProposalAttachment.StreamUrl"/>.
	/// </summary>
	Task<string?> GetStreamUrlAsync(int attachmentId, CancellationToken cancellationToken = default);
}
