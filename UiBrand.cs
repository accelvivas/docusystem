namespace docusystem;

/// <summary>Navy + white palette for the governance app (plus light navy lines and muted navy text for readability).</summary>
public static class UiBrand
{
	public static readonly Color Navy = Color.FromArgb("#003087");
	public static readonly Color NavyDeep = Color.FromArgb("#002165");
	public static readonly Color NavyMid = Color.FromArgb("#1A4BA0");
	/// <summary>Very light navy wash on white (same idea as PrimaryLight).</summary>
	public static readonly Color NavyWash = Color.FromArgb("#EEF2FA");
	public static readonly Color White = Colors.White;

	/// <summary>Navy at ~55% opacity on white for secondary labels.</summary>
	public static readonly Color NavyMutedText = Color.FromArgb("#8C9DB8");

	public static readonly Color NavyLine = Color.FromArgb("#C5D3E8");

	/// <summary>Success / completed-step green (align with <c>SuccessColor</c> in app resources).</summary>
	public static readonly Color Success = Color.FromArgb("#16A34A");
	public static readonly Color SuccessLight = Color.FromArgb("#ECFDF5");
	public static readonly Color SuccessBorder = Color.FromArgb("#6EE7B7");
	public static readonly Color SuccessText = Color.FromArgb("#166534");
	public static readonly Color SuccessSubtext = Color.FromArgb("#15803D");
}
