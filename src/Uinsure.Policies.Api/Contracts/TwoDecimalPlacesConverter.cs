using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Uinsure.Policies.Api.Contracts;

// Every money amount is written with exactly two decimal places (803.40, not 803.4). Formatting only: amounts
// reach the Api already rounded by Money.Round, so "F2" only pads and never changes a value. Reading stays
// strict: only a JSON number is accepted.
public sealed class TwoDecimalPlacesConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Number ? reader.GetDecimal() : throw new JsonException("Expected a number.");

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString("F2", CultureInfo.InvariantCulture));
}
