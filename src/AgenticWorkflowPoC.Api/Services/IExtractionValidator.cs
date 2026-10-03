using System;
using AgenticWorkflowPoC.Api.Models;

namespace AgenticWorkflowPoC.Api.Services
{
    public record ExtractionResult(bool IsValid, string? StaffId = null, DateTimeOffset? Date = null, string? Error = null);

    public interface IExtractionValidator
    {
        ExtractionResult Validate(string jsonContent);
    }
}
