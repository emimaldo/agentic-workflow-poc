using System.Text.Json;

namespace AgenticWorkflowPoC.Api.Services
{
    public class ExtractionValidator : IExtractionValidator
    {
        public ExtractionResult Validate(string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                return new ExtractionResult(false, Error: "No content");
            }

            try
            {
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out var _))
                {
                    return new ExtractionResult(false, Error: "missing_data");
                }

                if (!root.TryGetProperty("staffId", out var staffIdProp) || !root.TryGetProperty("date", out var dateProp))
                {
                    return new ExtractionResult(false, Error: "missing_fields");
                }

                var staffId = staffIdProp.GetString();
                var date = dateProp.GetString();

                if (string.IsNullOrWhiteSpace(staffId) || string.IsNullOrWhiteSpace(date))
                {
                    return new ExtractionResult(false, Error: "incomplete_data");
                }

                return new ExtractionResult(true, StaffId: staffId, Date: date);
            }
            catch (JsonException)
            {
                return new ExtractionResult(false, Error: "invalid_json");
            }
        }
    }
}
