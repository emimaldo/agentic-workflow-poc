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
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                return new ExtractionResult(false, Error: "No content");
            }

            try
            {
                var extraction = JsonSerializer.Deserialize<ExtractionPayload>(jsonContent, JsonOptions);

                if (extraction is null)
                {
                    return new ExtractionResult(false, Error: "empty_payload");
                }

                if (!string.IsNullOrWhiteSpace(extraction.Error))
                {
                    return new ExtractionResult(false, Error: "missing_data");
                }

                if (string.IsNullOrWhiteSpace(extraction.StaffId) || string.IsNullOrWhiteSpace(extraction.Date))
                {
                    return new ExtractionResult(false, Error: "missing_fields");
                }

                if (!DateTimeOffset.TryParse(
                    extraction.Date,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var date))
                {
                    return new ExtractionResult(false, Error: "invalid_date");
                }

                return new ExtractionResult(true, StaffId: extraction.StaffId, Date: date);
            }
            catch (JsonException)
            {
                return new ExtractionResult(false, Error: "invalid_json");
            }
        }

        private sealed class ExtractionPayload
        {
            public string? StaffId { get; init; }
            public string? Date { get; init; }
            public string? Error { get; init; }
        }
    }
}
