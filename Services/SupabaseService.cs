using Supabase;

namespace docusystem.Services;

/// <summary>
/// Optional Supabase client — available when <c>Supabase:Url</c> and <c>Supabase:AnonKey</c> are set.
/// Use <see cref="Client.Auth"/> for sign-in, <see cref="Client.From{TModel}"/> for tables, <see cref="Client.Storage"/> for files.
/// </summary>
public sealed class SupabaseService
{
	private readonly Client? _client;

	public Client? Client => _client;

	public bool IsAvailable => _client is not null;

	public SupabaseService(SupabaseSettings settings)
	{
		if (!settings.IsConfigured)
		{
			return;
		}

		_client = new Client(settings.Url, settings.AnonKey, new SupabaseOptions
		{
			AutoConnectRealtime = false
		});

		_client.InitializeAsync().GetAwaiter().GetResult();
	}
}
