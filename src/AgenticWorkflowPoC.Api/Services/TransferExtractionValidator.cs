using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Security;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Core.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgenticWorkflowPoC.Api.Services
{
    /// <summary>
    /// Semantic orchestrator + anti-corruption layer for transfer intents (Chapter 2).
    /// The LLM is used strictly as a parser: structured output is enforced at the inference
    /// level, tool calling is disabled, and every payload is validated in C# before the
    /// domain layer sees it. TenantId and source account come from the authenticated context.
    /// </summary>
    public sealed class TransferExtractionValidator : IExtractionValidator<EnrichedTransferCommand>
    {
        private const int MaxPayloadLength = 4096;

        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow // Blocks hallucinated keys
        };

        private readonly AIAgent _extractionAgent;
        private readonly ITenantAccessor _tenantAccessor;
        private readonly ITenantPolicyProvider _policyProvider;

        public TransferExtractionValidator(
            AIAgent extractionAgent,
            ITenantAccessor tenantAccessor,
            ITenantPolicyProvider policyProvider)
        {
            _extractionAgent = extractionAgent;
            _tenantAccessor = tenantAccessor;
            _policyProvider = policyProvider;
        }

        /// <summary>Inference contract: JSON schema, zero temperature, no tools, no autonomous invocation.</summary>
        public static ChatOptions CreateChatOptions() => new()
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema<TransferRequestPayload>(
                JsonOptions,
                schemaName: "transfer_request_schema",
                schemaDescription: "Strict schema for extracting financial transfer intents"),
            Temperature = 0f,
            ToolMode = ChatToolMode.None,
            Tools = []
        };

        public async Task<ExtractionResult<EnrichedTransferCommand>> ExtractAndValidateAsync(
            string userIntent,
            CancellationToken cancellationToken = default)
        {
            // 1. Deterministic semantic extraction (structured output, no tools).
            var response = await _extractionAgent.RunAsync(
                userIntent,
                options: new ChatClientAgentRunOptions { ChatOptions = CreateChatOptions() },
                cancellationToken: cancellationToken);

            var json = SanitizeJsonPayload(response.Text);
            if (json.Length == 0)
            {
                return Fail(ExtractionErrorCode.EmptyResponse, "The model returned no usable payload.");
            }

            if (json.Length > MaxPayloadLength)
            {
                return Fail(ExtractionErrorCode.SchemaViolation, "The model payload exceeds the allowed size.");
            }

            // 2. Strict deserialization. Exception details are not surfaced (no internal leakage).
            TransferRequestPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<TransferRequestPayload>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return Fail(ExtractionErrorCode.SchemaViolation, "The model output does not match the transfer schema.");
            }

            if (payload is null)
            {
                return Fail(ExtractionErrorCode.EmptyResponse, "The model returned an empty payload.");
            }

            if (string.IsNullOrWhiteSpace(payload.DestinationAccount) || string.IsNullOrWhiteSpace(payload.Currency))
            {
                return Fail(ExtractionErrorCode.MissingField, "The destination account and currency must be stated explicitly.");
            }

            var normalized = payload with
            {
                DestinationAccount = NormalizeAccount(payload.DestinationAccount),
                Currency = payload.Currency.Trim().ToUpperInvariant()
            };

            // 3. Context enrichment (multi-tenant isolation): never taken from the model.
            var tenantId = _tenantAccessor.GetCurrentTenantId();
            var policy = _policyProvider.GetRequiredPolicy(tenantId);

            return ExtractionResult<EnrichedTransferCommand>.Success(new EnrichedTransferCommand
            {
                DestinationAccount = normalized.DestinationAccount,
                Amount = normalized.Amount,
                Currency = normalized.Currency,
                TenantId = tenantId,
                SourceAccountId = policy.OperatingAccountId
            });
        }

        internal static string NormalizeAccount(string account) =>
            string.Concat(account.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

        /// <summary>
        /// Defense-in-depth for local/open-source models that wrap JSON in Markdown fences.
        /// </summary>
        internal static string SanitizeJsonPayload(string? payload)
        {
            var trimmed = (payload ?? string.Empty).Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLineEnd = trimmed.IndexOf('\n');
                trimmed = firstLineEnd < 0 ? string.Empty : trimmed[(firstLineEnd + 1)..];
                if (trimmed.EndsWith("```", StringComparison.Ordinal))
                {
                    trimmed = trimmed[..^3];
                }
            }

            return trimmed.Trim();
        }

        private static ExtractionResult<EnrichedTransferCommand> Fail(ExtractionErrorCode code, string message) =>
            ExtractionResult<EnrichedTransferCommand>.Failure(code, message);
    }
}
