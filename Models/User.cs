using System.Text.Json.Serialization;
using docusystem.Serialization;

namespace docusystem.Models;

/// <summary>
/// Authenticated user — map fields to your Laravel API resource (e.g. Sanctum user JSON).
/// </summary>
public class User
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	/// <summary>Laravel often uses "name" or "full_name" — adjust JsonPropertyName to match your API.</summary>
	[JsonPropertyName("full_name")]
	public string FullName { get; set; } = string.Empty;

	/// <summary>Default Eloquent <c>users.name</c> (many APIs omit <c>full_name</c>).</summary>
	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("first_name")]
	public string? FirstName { get; set; }

	[JsonPropertyName("last_name")]
	public string? LastName { get; set; }

	[JsonPropertyName("email")]
	public string Email { get; set; } = string.Empty;

	/// <summary>From API <c>role</c> (object, string slug, or id).</summary>
	[JsonPropertyName("role")]
	[JsonConverter(typeof(UserRoleJsonConverter))]
	public UserRole? UserRole { get; set; }

	/// <summary>Spatie / other stacks that expose <c>roles</c> as an array; first entry is used when <see cref="UserRole"/> is null.</summary>
	[JsonPropertyName("roles")]
	[JsonConverter(typeof(UserRoleListJsonConverter))]
	public List<UserRole>? Roles { get; set; }

	/// <summary>
	/// <c>users.role_type</c> (e.g. NULP schema: <c>ORG_OFFICER</c>, <c>APPROVER</c>, <c>ADMIN</c>).
	/// </summary>
	[JsonPropertyName("role_type")]
	public string? RoleType { get; set; }

	/// <summary>Same as <see cref="RoleType"/> when the API returns camelCase JSON.</summary>
	[JsonPropertyName("roleType")]
	public string? RoleTypeCamel { get; set; }

	[JsonPropertyName("school_id")]
	public string? SchoolId { get; set; }

	[JsonPropertyName("user_role")]
	public string? UserRoleSlug { get; set; }

	[JsonPropertyName("type")]
	public string? Type { get; set; }

	[JsonPropertyName("account_type")]
	public string? AccountType { get; set; }

	/// <summary>FK to <c>roles.id</c> when the API only sends an id, not a nested <c>role</c> object.</summary>
	[JsonPropertyName("role_id")]
	public int? RoleId { get; set; }

	[JsonPropertyName("roleId")]
	public int? RoleIdCamel { get; set; }

	/// <summary>Some APIs use <c>current_role</c> instead of <c>role</c>.</summary>
	[JsonPropertyName("current_role")]
	[JsonConverter(typeof(UserRoleJsonConverter))]
	public UserRole? CurrentRole { get; set; }

	/// <summary>Human-friendly role label for UI and for legacy checks (e.g. "RSO President"). Not sent as top-level JSON — derived from <see cref="UserRole"/>.</summary>
	[JsonIgnore]
	public string Role => GetRoleLabel();

	/// <summary>From <c>roles.approval_level</c> when the API includes the <c>role</c> relation (NULL e.g. for RSO President).</summary>
	[JsonIgnore]
	public int? RoleApprovalLevel =>
		UserRole?.ApprovalLevel ??
		CurrentRole?.ApprovalLevel ??
		(Roles is { Count: > 0 } ? Roles[0]?.ApprovalLevel : null);

	[JsonIgnore]
	private int? ResolvedRoleId => RoleId ?? RoleIdCamel;

	/// <summary>Machine slug — <c>roles.name</c> (e.g. rso_president) first, then legacy NULP <c>role_type</c>.</summary>
	[JsonIgnore]
	public string RoleKey
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(UserRole?.Name))
			{
				return UserRole!.Name;
			}

			if (!string.IsNullOrWhiteSpace(CurrentRole?.Name))
			{
				return CurrentRole!.Name;
			}

			if (Roles is not null && Roles.Count > 0 && !string.IsNullOrWhiteSpace(Roles[0]?.Name))
			{
				return Roles[0]!.Name;
			}

			if (ResolvedRoleId is int r && r > 0 && RoleIdCatalog.TryGetNameSlug(r, out var slug))
			{
				return slug;
			}

			return FirstNonEmpty(
				RoleType,
				RoleTypeCamel,
				UserRoleSlug,
				Type,
				AccountType) ?? string.Empty;
		}
	}

	/// <summary>RSO / organization the user represents (e.g. RSO President scope). From Laravel <c>organization_name</c> on the user or membership.</summary>
	[JsonPropertyName("organization_name")]
	public string? OrganizationName { get; set; }

	/// <summary>Name for UI — uses full_name from API, or first + last, or email local-part.</summary>
	[JsonIgnore]
	public string DisplayName
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(FullName))
			{
				return FullName.Trim();
			}

			if (!string.IsNullOrWhiteSpace(Name))
			{
				return Name.Trim();
			}

			var combined = $"{FirstName} {LastName}".Trim();
			if (!string.IsNullOrWhiteSpace(combined))
			{
				return combined;
			}

			if (!string.IsNullOrWhiteSpace(Email) && Email.Contains('@', StringComparison.Ordinal))
			{
				return Email[..Email.IndexOf('@', StringComparison.Ordinal)];
			}

			return "User";
		}
	}

	private string GetRoleLabel()
	{
		// 1) `roles` table: nested `role` or `current_role` { id, name, display_name, approval_level }
		var primary = UserRole ?? CurrentRole;
		if (primary is not null)
		{
			var from = LabelFromUserRoleObject(primary);
			if (!string.IsNullOrEmpty(from))
			{
				return from;
			}
		}

		// 2) Spatie list or array of `roles` rows
		if (Roles is { Count: > 0 } list)
		{
			for (var i = 0; i < list.Count; i++)
			{
				var r = list[i];
				if (r is null)
				{
					continue;
				}

				var s = LabelFromUserRoleObject(r);
				if (!string.IsNullOrEmpty(s))
				{
					return s;
				}
			}
		}

		// 3) NULP: users.role_type ENUM (wala pa ring `role` relation sa JSON)
		var columnSlug = FirstNonEmpty(RoleType, RoleTypeCamel, UserRoleSlug, Type, AccountType);
		if (!string.IsNullOrWhiteSpace(columnSlug))
		{
			return MapOrTitleSlug(columnSlug.Trim());
		}

		// 4) API only sent users.role_id (no nested object)
		if (ResolvedRoleId is int id && id > 0 && RoleIdCatalog.TryGetDisplayName(id, out var fromCatalog))
		{
			return fromCatalog;
		}

		return string.Empty;
	}

	private static string? FirstNonEmpty(params string?[] values)
	{
		for (var i = 0; i < values.Length; i++)
		{
			var v = values[i];
			if (!string.IsNullOrWhiteSpace(v))
			{
				return v.Trim();
			}
		}

		return null;
	}

	private static string LabelFromUserRoleObject(UserRole r)
	{
		if (!string.IsNullOrWhiteSpace(r.DisplayName))
		{
			return r.DisplayName.Trim();
		}

		if (!string.IsNullOrWhiteSpace(r.Name))
		{
			return MapOrTitleSlug(r.Name.Trim());
		}

		if (r.Id > 0 && RoleIdCatalog.TryGetDisplayName(r.Id, out var fromId))
		{
			return fromId;
		}

		return string.Empty;
	}

	private static string MapOrTitleSlug(string fromName)
	{
		if (string.IsNullOrEmpty(fromName))
		{
			return string.Empty;
		}

		var key = fromName.Trim().ToLowerInvariant();
		// Matches `roles` seed (display_name) when the API only sends a slug, not a nested role row.
		var mapped = key switch
		{
			"rso_president" => "RSO President",
			"adviser" => "Adviser",
			"program_chair" => "Program Chair",
			"dean" => "Dean",
			"academic_director" => "Academic Director",
			"executive_director" => "Executive Director",
			"sdao_staff" => "SDAO Staff",
			"admin" => "Admin",
			// NULP sdao: users.role_type ENUM
			"org_officer" => "Organization Officer",
			"approver" => "Approver",
			_ => string.Empty
		};

		if (!string.IsNullOrEmpty(mapped))
		{
			return mapped;
		}

		return ToTitleFromSnake(fromName);
	}

	private static string ToTitleFromSnake(string? snake)
	{
		if (string.IsNullOrWhiteSpace(snake))
		{
			return string.Empty;
		}

		var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length == 0)
		{
			return snake;
		}

		return string.Join(" ", parts.Select(static p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
	}
}
