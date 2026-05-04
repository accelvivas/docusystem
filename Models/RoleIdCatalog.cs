namespace docusystem.Models;

/// <summary>
/// Default rows for <c>roles</c> (id, name, display_name) when the API only returns <c>role_id</c> or a role row with <c>id</c> and no <c>name</c> yet.
/// Aligned with Laravel/Postgres seeds: <c>rso_president</c> … <c>admin</c>, <c>student</c>.
/// </summary>
public static class RoleIdCatalog
{
	/// <summary>Matches typical <c>roles.id</c> / <c>users.role_id</c> (1…9).</summary>
	public static bool TryGetDisplayName(int roleId, out string displayName)
	{
		displayName = roleId switch
		{
			1 => "RSO President",
			2 => "Adviser",
			3 => "Program Chair",
			4 => "Dean",
			5 => "Academic Director",
			6 => "Executive Director",
			7 => "SDAO Staff",
			8 => "Admin",
			9 => "Student",
			_ => string.Empty
		};

		return displayName.Length > 0;
	}

	public static bool TryGetNameSlug(int roleId, out string slug)
	{
		slug = roleId switch
		{
			1 => "rso_president",
			2 => "adviser",
			3 => "program_chair",
			4 => "dean",
			5 => "academic_director",
			6 => "executive_director",
			7 => "sdao_staff",
			8 => "admin",
			9 => "student",
			_ => string.Empty
		};

		return slug.Length > 0;
	}
}
