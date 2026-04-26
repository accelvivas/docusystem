using System.Text.Json;
using docusystem.Models;

namespace docusystem.Serialization;

/// <summary>
/// Resolves a <see cref="User"/> from common Laravel response shapes: plain user JSON,
/// <c>{ "user": { ... } }</c>, and <c>{ "data": { ... } }</c> / <c>{ "data": { "user": ... } }</c>.
/// </summary>
public static class ApiUserJson
{
	/// <summary>Parse root object and return the first element that deserializes to <see cref="User"/> with an email.</summary>
	public static User? DeserializeUser(string? json, JsonSerializerOptions options)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			using var doc = JsonDocument.Parse(json);
			return DeserializeUser(doc.RootElement, options);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public static User? DeserializeUser(JsonElement root, JsonSerializerOptions options)
	{
		if (root.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		// { "user": { "email": "..." } } — most login payloads
		if (root.TryGetProperty("user", out var userEl) && IsUserWithEmail(userEl))
		{
			return JsonSerializer.Deserialize<User>(userEl.GetRawText(), options);
		}

		// { "data": { "user": { ... } } } or { "data": { "email": "..." } } or JSON:API
		// { "data": { "attributes": { "email": "..." } } }
		if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
		{
			if (data.TryGetProperty("user", out var dataUser) && IsUserWithEmail(dataUser))
			{
				return JsonSerializer.Deserialize<User>(dataUser.GetRawText(), options);
			}

			if (data.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object && IsUserWithEmail(attributes))
			{
				return JsonSerializer.Deserialize<User>(attributes.GetRawText(), options);
			}

			if (IsUserWithEmail(data))
			{
				return JsonSerializer.Deserialize<User>(data.GetRawText(), options);
			}
		}

		// Root is the user (GET /user or Resource without wrapper)
		if (IsUserWithEmail(root))
		{
			return JsonSerializer.Deserialize<User>(root.GetRawText(), options);
		}

		return null;
	}

	private static bool IsUserWithEmail(JsonElement e) =>
		e.ValueKind == JsonValueKind.Object &&
		e.TryGetProperty("email", out var em) &&
		em.ValueKind == JsonValueKind.String &&
		!string.IsNullOrWhiteSpace(em.GetString());
}
