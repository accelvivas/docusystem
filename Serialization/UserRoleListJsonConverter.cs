using System.Text.Json;
using System.Text.Json.Serialization;
using docusystem.Models;

namespace docusystem.Serialization;

/// <summary>
/// Spatie sometimes serializes <c>roles</c> as an array of strings; otherwise each item is a role object.
/// The default <see cref="List{UserRole}"/> serializer would throw on string elements, causing the whole
/// <see cref="User"/> parse to fail and the app to keep the old session.
/// </summary>
public sealed class UserRoleListJsonConverter : JsonConverter<List<UserRole>?>
{
	public override List<UserRole>? Read(
		ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}

		if (reader.TokenType != JsonTokenType.StartArray)
		{
			reader.Skip();
			return null;
		}

		var list = new List<UserRole>();
		while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
		{
			switch (reader.TokenType)
			{
				case JsonTokenType.String:
					var s = reader.GetString();
					if (!string.IsNullOrWhiteSpace(s))
					{
						list.Add(new UserRole { Name = s.Trim() });
					}
					else
					{
						list.Add(new UserRole());
					}

					break;
				case JsonTokenType.StartObject:
					var o = JsonSerializer.Deserialize<UserRole>(ref reader, options) ?? new UserRole();
					list.Add(o);
					break;
				default:
					reader.Skip();
					break;
			}
		}

		return list;
	}

	public override void Write(Utf8JsonWriter writer, List<UserRole>? value, JsonSerializerOptions options)
	{
		if (value is null)
		{
			writer.WriteNullValue();
			return;
		}

		writer.WriteStartArray();
		for (var i = 0; i < value.Count; i++)
		{
			JsonSerializer.Serialize(writer, value[i], options);
		}

		writer.WriteEndArray();
	}
}
