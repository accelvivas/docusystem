using System.Text.Json;
using System.Text.Json.Serialization;
using docusystem.Models;

namespace docusystem.Serialization;

/// <summary>
/// Deserializes Laravel <c>role</c> whether the API sends an object, a string slug, or a numeric id.
/// </summary>
public sealed class UserRoleJsonConverter : JsonConverter<UserRole?>
{
	public override UserRole? Read(
		ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		switch (reader.TokenType)
		{
			case JsonTokenType.Null:
				return null;
			case JsonTokenType.String:
			{
				var s = reader.GetString();
				if (string.IsNullOrWhiteSpace(s))
				{
					return null;
				}

				return new UserRole { Name = s.Trim() };
			}
			case JsonTokenType.Number:
			{
				if (reader.TryGetInt32(out var id))
				{
					return new UserRole { Id = id };
				}

				reader.Skip();
				return null;
			}
			case JsonTokenType.StartObject:
				return JsonSerializer.Deserialize<UserRole>(ref reader, options);
			case JsonTokenType.StartArray:
			{
				// e.g. misconfigured API sends role: []
				reader.Skip();
				return null;
			}
			default:
				reader.Skip();
				return null;
		}
	}

	public override void Write(Utf8JsonWriter writer, UserRole? value, JsonSerializerOptions options)
	{
		if (value is null)
		{
			writer.WriteNullValue();
			return;
		}

		JsonSerializer.Serialize(writer, value, options);
	}
}
