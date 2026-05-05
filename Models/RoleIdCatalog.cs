namespace docusystem.Models;

/// <summary>
/// Fallback when the API sends <c>role_id</c> without a full nested <c>role</c> row.
/// Keep aligned with your <c>roles</c> seed (Supabase/Laravel).
/// </summary>
public static class RoleIdCatalog
{
	/// <summary>Matches <c>roles.id</c> / <c>users.role_id</c> (1…10).</summary>
	public static bool TryGetDisplayName(int roleId, out string displayName)
	{
		displayName = roleId switch
		{
			1 => "Student",
			2 => "RSO President",
			3 => "Adviser",
			4 => "Program Chair",
			5 => "Dean",
			6 => "SDAO Staff",
			7 => "Assistant Director",
			8 => "Academic Director",
			9 => "Executive Director",
			10 => "Admin",
			_ => string.Empty
		};

		return displayName.Length > 0;
	}

	public static bool TryGetNameSlug(int roleId, out string slug)
	{
		slug = roleId switch
		{
			1 => "student",
			2 => "rso_president",
			3 => "adviser",
			4 => "program_chair",
			5 => "dean",
			6 => "sdao_staff",
			7 => "assistant_director",
			8 => "academic_director",
			9 => "executive_director",
			10 => "admin",
			_ => string.Empty
		};

		return slug.Length > 0;
	}
}
