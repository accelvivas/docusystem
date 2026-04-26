using System.Text.Json;
using Supabase.Gotrue;

namespace docusystem.Services;

/// <summary>Maps Supabase Auth <see cref="Supabase.Gotrue.User"/> into app <see cref="Models.User"/>.</summary>
public static class SupabaseUserMapper
{
	public static Models.User ToAppUser(Supabase.Gotrue.User gotrueUser)
	{
		var email = gotrueUser.Email ?? string.Empty;
		var roleMeta = GetStringMetadata(gotrueUser, "role");
		return new Models.User
		{
			Id = 0,
			Email = email,
			FullName = GetStringMetadata(gotrueUser, "full_name", "name") ?? email.Split('@')[0],
			UserRole = string.IsNullOrWhiteSpace(roleMeta)
				? null
				: new Models.UserRole { Name = roleMeta, DisplayName = roleMeta },
			OrganizationName = GetStringMetadata(gotrueUser, "organization_name")
		};
	}

	private static string? GetStringMetadata(Supabase.Gotrue.User u, params string[] keys)
	{
		if (u.UserMetadata is null)
		{
			return null;
		}

		foreach (var key in keys)
		{
			if (!u.UserMetadata.TryGetValue(key, out var value) || value is null)
			{
				continue;
			}

			if (value is string s)
			{
				return s;
			}

			if (value is JsonElement je && je.ValueKind == JsonValueKind.String)
			{
				return je.GetString();
			}

			return value.ToString();
		}

		return null;
	}
}
