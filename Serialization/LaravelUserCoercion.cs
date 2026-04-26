using System;
using System.Text.Json;
using docusystem.Models;

namespace docusystem.Serialization;

/// <summary>
/// Last-resort parse when full <see cref="User"/> deserialization fails but JSON has <c>email</c> and optional <c>role</c> / <c>role_id</c>.
/// </summary>
public static class LaravelUserCoercion
{
	public static User? TryBuild(string? json, JsonSerializerOptions options)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			using var doc = JsonDocument.Parse(json);
			return TryBuild(doc.RootElement, options);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public static User? TryBuild(JsonElement root, JsonSerializerOptions options)
	{
		foreach (var candidate in EnumerateUserCandidates(root))
		{
			var u = TryObjectAsUser(candidate, options);
			if (u is not null && !string.IsNullOrWhiteSpace(u.Email))
			{
				return u;
			}
		}

		return null;
	}

	private static IEnumerable<JsonElement> EnumerateUserCandidates(JsonElement root)
	{
		yield return root;
		if (root.ValueKind != JsonValueKind.Object)
		{
			yield break;
		}

		if (root.TryGetProperty("data", out var data))
		{
			yield return data;
			if (data.ValueKind == JsonValueKind.Object)
			{
				if (data.TryGetProperty("user", out var dataUser))
				{
					yield return dataUser;
				}

				if (data.TryGetProperty("attributes", out var attrs))
				{
					yield return attrs;
				}
			}
		}

		if (root.TryGetProperty("user", out var user))
		{
			yield return user;
		}
	}

	private static User? TryObjectAsUser(JsonElement e, JsonSerializerOptions options)
	{
		if (e.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		if (!e.TryGetProperty("email", out var em) || em.ValueKind != JsonValueKind.String)
		{
			return null;
		}

		var email = em.GetString();
		if (string.IsNullOrWhiteSpace(email))
		{
			return null;
		}

		var u = new User { Email = email.Trim() };
		if (e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var uid))
		{
			u.Id = uid;
		}

		SetString(e, "first_name", v => u.FirstName = v);
		SetString(e, "last_name", v => u.LastName = v);
		SetString(e, "name", v => u.Name = v);
		SetString(e, "full_name", v => u.FullName = v);
		SetString(e, "school_id", v => u.SchoolId = v);
		SetString(e, "role_type", v => u.RoleType = v);
		SetString(e, "organization_name", v => u.OrganizationName = v);

		if (e.TryGetProperty("role_id", out var rid) && rid.TryGetInt32(out var rida))
		{
			u.RoleId = rida;
		}

		if (e.TryGetProperty("roleId", out var rid2) && rid2.TryGetInt32(out var ridb))
		{
			u.RoleIdCamel = ridb;
		}

		if (e.TryGetProperty("role", out var roleEl))
		{
			try
			{
				if (roleEl.ValueKind == JsonValueKind.Object)
				{
					u.UserRole = JsonSerializer.Deserialize<UserRole>(roleEl.GetRawText(), options);
				}
				else if (roleEl.ValueKind == JsonValueKind.String)
				{
					var s = roleEl.GetString();
					if (!string.IsNullOrWhiteSpace(s))
					{
						u.UserRole = new UserRole { Name = s.Trim() };
					}
				}
				else if (roleEl.ValueKind == JsonValueKind.Number && roleEl.TryGetInt32(out var roleId))
				{
					u.UserRole = new UserRole { Id = roleId };
				}
			}
			catch (JsonException)
			{
				// ignore partial role
			}
		}

		if (e.TryGetProperty("current_role", out var cr) && cr.ValueKind == JsonValueKind.Object)
		{
			try
			{
				u.CurrentRole = JsonSerializer.Deserialize<UserRole>(cr.GetRawText(), options);
			}
			catch (JsonException)
			{
			}
		}

		if (e.TryGetProperty("currentRole", out var cr2) && cr2.ValueKind == JsonValueKind.Object)
		{
			try
			{
				u.CurrentRole ??= JsonSerializer.Deserialize<UserRole>(cr2.GetRawText(), options);
			}
			catch (JsonException)
			{
			}
		}

		return u;
	}

	private static void SetString(JsonElement e, string name, Action<string> set)
	{
		if (!e.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
		{
			return;
		}

		var s = p.GetString();
		if (!string.IsNullOrWhiteSpace(s))
		{
			set(s.Trim());
		}
	}
}
