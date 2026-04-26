namespace docusystem.Services;

/// <summary>Startup settings loaded from bundled <c>appsettings.json</c>.</summary>
public sealed class AppConfiguration
{
	public ApiEndpointOptions Api { get; } = new();

	public SupabaseSettings Supabase { get; } = new();

	public MobileDataOptions Data { get; } = new();

	public AuthOptions Auth { get; } = new();
}
