using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Erp.Api.Common;

/// <summary>
/// Every DateTime in ErpDb is stored in UTC (SYSUTCDATETIME / TimeProvider.GetUtcNow),
/// but values read back from SQL Server have DateTimeKind.Unspecified, so the
/// default serializer writes "2026-11-26T17:39:00" with no "Z". Browsers then
/// read it as LOCAL time: an approval made at 23:09 IST showed as 17:39.
/// This converter always writes ISO-8601 with "Z", so the Angular date pipe
/// converts to the user's time zone correctly.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        DateTime utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)        // Unspecified from SQL = UTC by convention
        };
        writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
    }
}
