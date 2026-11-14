namespace AgenticWorkflowPoC.Api.Services
{
    internal static class AgentDefaults
    {
        public const string ExtractionPrompt =
            "You are a data extraction API. Extract the staff ID and date from the user's prompt. " +
            "Return only the fields staffId and date as JSON. The date must be ISO 8601 and include a timezone offset. " +
            "Do not infer missing values; leave them null.";
    }
}
