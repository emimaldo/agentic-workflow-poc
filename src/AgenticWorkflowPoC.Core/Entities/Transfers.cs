namespace AgenticWorkflowPoC.Core.Entities
{
    /// <summary>
    /// Strict schema the LLM is allowed to produce (Chapter 2).
    /// TenantId and the source account are deliberately excluded: they are never
    /// taken from the model, only from the authenticated request context.
    /// </summary>
    public record TransferRequestPayload
    {
        public required string DestinationAccount { get; init; }
        public required decimal Amount { get; init; }
        public required string Currency { get; init; }
    }

    /// <summary>
    /// Internal domain command, enriched by deterministic C# after validation.
    /// </summary>
    public sealed record EnrichedTransferCommand : TransferRequestPayload
    {
        public required string TenantId { get; init; }
        public required string SourceAccountId { get; init; }
    }
}
