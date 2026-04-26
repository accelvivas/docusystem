using System.Text.Json;

namespace docusystem.Services;

/// <summary>Loads <c>Resources/Raw/appsettings.json</c> at startup (MauiAsset logical name <c>appsettings.json</c>).</summary>
public static class ApiSettingsLoader
{
	private const string AppSettingsFileName = "appsettings.json";

	public static AppConfiguration Load()
	{
		var config = new AppConfiguration();
		try
		{
			using var stream = FileSystem.Current.OpenAppPackageFileAsync(AppSettingsFileName).GetAwaiter().GetResult();
			using var doc = JsonDocument.Parse(stream);
			var root = doc.RootElement;

			if (root.TryGetProperty("Api", out var api))
			{
				if (api.TryGetProperty("LaravelBaseUrl", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
				{
					var u = urlEl.GetString();
					if (!string.IsNullOrWhiteSpace(u))
					{
						config.Api.LaravelBaseUrl = ApiEndpointOptions.NormalizeBaseUrl(u);
					}
				}
			}

			if (root.TryGetProperty("Supabase", out var supabase))
			{
				if (supabase.TryGetProperty("Url", out var sbUrl) && sbUrl.ValueKind == JsonValueKind.String)
				{
					var u = sbUrl.GetString();
					if (!string.IsNullOrWhiteSpace(u))
					{
						config.Supabase.Url = u.Trim().TrimEnd('/');
					}
				}

				if (supabase.TryGetProperty("AnonKey", out var keyEl) && keyEl.ValueKind == JsonValueKind.String)
				{
					var k = keyEl.GetString();
					if (!string.IsNullOrWhiteSpace(k))
					{
						config.Supabase.AnonKey = k.Trim();
					}
				}
			}

			if (root.TryGetProperty("Data", out var data) &&
			    data.TryGetProperty("Backend", out var be) &&
			    be.ValueKind == JsonValueKind.String)
			{
				var s = be.GetString();
				if (!string.IsNullOrWhiteSpace(s))
				{
					config.Data.Backend = s.Trim();
				}
			}

			if (root.TryGetProperty("Auth", out var auth) &&
			    auth.TryGetProperty("Provider", out var prov) &&
			    prov.ValueKind == JsonValueKind.String)
			{
				var s = prov.GetString();
				if (!string.IsNullOrWhiteSpace(s))
				{
					config.Auth.Provider = s.Trim();
				}
			}
		}
		catch
		{
			// Missing or invalid appsettings — defaults + preferences still apply.
		}

		config.Api.ApplyPreferencesOverride();
		config.Api.RemapLocalhostForAndroidEmulator();
		return config;
	}
}
