namespace docusystem.Services;

/// <summary>Project URL and anon key from <c>appsettings.json</c> — use the public anon key only (never the service role key in the app).</summary>
public sealed class SupabaseSettings
{
	public string Url { get; set; } = string.Empty;

	public string AnonKey { get; set; } = string.Empty;

	public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(AnonKey);
}
