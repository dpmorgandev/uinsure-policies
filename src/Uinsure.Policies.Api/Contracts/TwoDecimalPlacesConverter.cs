using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Uinsure.Policies.Api.Contracts;

// Every money amount is written with at least two decimal places (803.40, not 803.4), and never rounded: trailing
// zeros beyond the second place are dropped (527.180 is 527.18, no value changes) and a value with more
// significant places is written exactly. Done on the text, because no decimal can hold every amount at scale 2.
// Reading stays strict: only a JSON number is accepted.
public sealed class TwoDecimalPlacesConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Number ? reader.GetDecimal() : throw new JsonException("Expected a number.");

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteRawValue(PadToTwoPlaces(value.ToString(CultureInfo.InvariantCulture)));

    private static string PadToTwoPlaces(string text)
    {
        var point = text.IndexOf('.');
        if (point < 0)
        {
            return text + ".00";
        }

        var trimmed = text.TrimEnd('0');
        var places = trimmed.Length - point - 1;
        return places >= 2 ? trimmed : trimmed + new string('0', 2 - places);
    }
}
