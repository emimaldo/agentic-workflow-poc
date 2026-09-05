namespace AgenticWorkflowPoC.Api.Services
{
    internal static class AgentDefaults
    {
        public const string ExtractionPrompt =
            "You are a data extraction API. Extract the staff ID and date from the user's prompt. " +
            "Respond ONLY with a valid JSON object in this exact format, with no markdown, no code blocks, and no extra text: " +
            "{\"staffId\": \"extracted_id\", \"date\": \"extracted_date\"}. " +
            "If no staff ID is found, return {\"error\": \"missing_data\"}.";
    }
}
