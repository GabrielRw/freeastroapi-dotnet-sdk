using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreeAstroApi.Serialization;

// Response schemas mark some fields as required and non-null based on samples, so the API can still send
// null for them. System.Text.Json already tolerates missing members, unknown members (kept in
// AdditionalProperties) and null for reference types; these converters make null (and numbers sent as
// strings) readable for the non-nullable value types the models use, instead of throwing.
// Serialization is unchanged.

internal sealed class LenientInt32Converter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null: return 0;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var value)) return value;
                if (reader.TryGetDouble(out var number) && number >= int.MinValue && number <= int.MaxValue) return (int)Math.Round(number);
                break;
            case JsonTokenType.String:
                if (double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    && parsed >= int.MinValue && parsed <= int.MaxValue)
                {
                    return (int)Math.Round(parsed);
                }
                break;
        }
        throw new JsonException($"Cannot read a {reader.TokenType} token as an integer.");
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

internal sealed class LenientInt64Converter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null: return 0;
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var value)) return value;
                if (reader.TryGetDouble(out var number) && number >= long.MinValue && number <= long.MaxValue) return (long)Math.Round(number);
                break;
            case JsonTokenType.String:
                var text = reader.GetString();
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exact)) return exact;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    && parsed >= long.MinValue && parsed <= long.MaxValue)
                {
                    return (long)Math.Round(parsed);
                }
                break;
        }
        throw new JsonException($"Cannot read a {reader.TokenType} token as an integer.");
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

internal sealed class LenientDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null: return 0;
            case JsonTokenType.Number: return reader.GetDouble();
            case JsonTokenType.String:
                if (double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
                break;
        }
        throw new JsonException($"Cannot read a {reader.TokenType} token as a number.");
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

internal sealed class LenientBooleanConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null: return false;
            case JsonTokenType.True: return true;
            case JsonTokenType.False: return false;
            case JsonTokenType.String:
                if (bool.TryParse(reader.GetString(), out var parsed)) return parsed;
                break;
        }
        throw new JsonException($"Cannot read a {reader.TokenType} token as a boolean.");
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}
