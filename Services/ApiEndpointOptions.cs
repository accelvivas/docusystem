using Microsoft.Maui.Devices;

namespace docusystem.Services;

/// <summary>Runtime API targets — loaded from <c>appsettings.json</c> and optional Preferences overrides.</summary>
public sealed class ApiEndpointOptions
{
	public string LaravelBaseUrl { get; set; } = "http://127.0.0.1:8000/";

	public void ApplyPreferencesOverride()
	{
		var url = Preferences.Get("docusystem_laravel_base_url", null);
		if (!string.IsNullOrWhiteSpace(url))
		{
			LaravelBaseUrl = NormalizeBaseUrl(url);
		}
	}

	/// <summary>
	/// <c>127.0.0.1</c> on the Android <b>emulator</b> is the emulator itself, not the dev PC. Map to <c>10.0.2.2</c>.
	/// Skipped on physical devices (they may use <c>adb reverse</c> with localhost or a LAN IP).
	/// </summary>
	public void RemapLocalhostForAndroidEmulator()
	{
		try
		{
			if (DeviceInfo.Platform != DevicePlatform.Android || DeviceInfo.DeviceType != DeviceType.Virtual)
			{
				return;
			}
		}
		catch
		{
			// e.g. DeviceInfo before platform is ready
			return;
		}

		if (!Uri.TryCreate(LaravelBaseUrl, UriKind.Absolute, out var u))
		{
			return;
		}

		if (u.Scheme != "http" && u.Scheme != "https")
		{
			return;
		}

		if (u.Host != "127.0.0.1" && u.Host != "localhost" && u.Host != "::1")
		{
			return;
		}

		var b = new UriBuilder(u) { Host = "10.0.2.2" };
		LaravelBaseUrl = NormalizeBaseUrl(b.Uri.ToString());
	}

	public static string NormalizeBaseUrl(string url)
	{
		var t = url.Trim();
		if (!t.EndsWith('/'))
		{
			t += '/';
		}

		return t;
	}
}
