namespace docusystem.Services;

/// <summary>Where the mobile app loads domain data: Supabase (PostgREST) or Laravel API.</summary>
public sealed class MobileDataOptions
{
	/// <summary><c>Supabase</c> = direct <see cref="SupabaseService"/>; <c>Laravel</c> = HTTP API.</summary>
	public string Backend { get; set; } = "Laravel";
}

/// <summary>Sign-in: Supabase Auth (email/password) or Laravel Sanctum API.</summary>
public sealed class AuthOptions
{
	/// <summary><c>Supabase</c> = <c>Client.Auth</c>; <c>Laravel</c> = <c>POST /api/login</c>.</summary>
	public string Provider { get; set; } = "Laravel";
}
