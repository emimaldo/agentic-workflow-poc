using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgenticWorkflowPoC.Api.Services
{
    public class ExtractionValidator : IExtractionValidator
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        public ExtractionResult Validate(string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(jsonContent) || jsonContent.Length > 4096)
            {
                return new ExtractionResult(false, Error: "invalid_payload_size");
            }

            try
            {
                var extraction = JsonSerializer.Deserialize<ExtractionPayload>(jsonContent, JsonOptions);

                if (extraction is null)
                {
                    return new ExtractionResult(false, Error: "empty_payload");
                }

                if (string.IsNullOrWhiteSpace(extraction.StaffId) || string.IsNullOrWhiteSpace(extraction.Date))
                {
                    return new ExtractionResult(false, Error: "missing_fields");
                }

                if (extraction.StaffId.Length > 100
                    || !DateTimeOffset.TryParse(
                        extraction.Date,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var date)
                    || !HasExplicitTimeZone(extraction.Date))
                {
                    return new ExtractionResult(false, Error: extraction.StaffId.Length > 100 ? "invalid_staff_id" : "invalid_date");
                }

                return new ExtractionResult(true, StaffId: extraction.StaffId, Date: date);
            }
            catch (JsonException)
            {
                return new ExtractionResult(false, Error: "invalid_json");
            }
        }

        private static bool HasExplicitTimeZone(string value)
        {
            var timeSeparator = value.IndexOf('T');
            return value.EndsWith('Z')
                || (timeSeparator >= 0
                    && (value.IndexOf('+', timeSeparator) >= 0
                        || value.IndexOf('-', timeSeparator) >= 0));
        }

        private sealed class ExtractionPayload
        {
            public string? StaffId { get; init; }
            public string? Date { get; init; }
        }
    }
}
