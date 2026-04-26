namespace docusystem.Services;

/// <summary>Attaches <c>Authorization: Bearer</c> for Laravel Sanctum when a token exists. Skips login requests.</summary>
public sealed class LaravelAuthDelegatingHandler : DelegatingHandler
{
	private readonly AppSessionService _session;

	public LaravelAuthDelegatingHandler(AppSessionService session)
	{
		_session = session;
	}

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (ShouldAttachBearer(request) && !string.IsNullOrEmpty(_session.AccessToken))
		{
			request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_session.AccessToken}");
		}

		return base.SendAsync(request, cancellationToken);
	}

	private static bool ShouldAttachBearer(HttpRequestMessage request)
	{
		if (request.RequestUri is null)
		{
			return false;
		}

		var path = request.RequestUri.AbsolutePath;
		return !path.Contains("/api/login", StringComparison.OrdinalIgnoreCase);
	}
}
