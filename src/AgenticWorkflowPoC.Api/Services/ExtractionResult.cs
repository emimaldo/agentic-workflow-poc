using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace AgenticWorkflowPoC.Api.Services
{
    /// <summary>
    /// Core validator contract for deterministic interception of LLM output (Chapter 2).
    /// Implementations own the inference call, enforce the JSON schema, validate domain
    /// invariants, grounding and tenant limits, and enrich the payload with the tenant context.
    /// </summary>
    public interface IExtractionValidator<T> where T : class
    {
        Task<ExtractionResult<T>> ExtractAndValidateAsync(string userIntent, CancellationToken cancellationToken = default);
    }

    public enum ExtractionErrorCode
    {
        EmptyResponse,
        SchemaViolation,
        MissingField,
        InvalidAccount,
        InvalidAmount,
        InvalidCurrency,
        Ungrounded,
        AmbiguousAmount,
        TenantLimitExceeded
    }

    public sealed record ExtractionError(ExtractionErrorCode Code, string Message);

    /// <summary>
    /// Named <c>ExtractionResult</c> (not <c>ValidationResult</c>) to avoid clashing with
    /// <see cref="System.ComponentModel.DataAnnotations.ValidationResult"/>.
    /// </summary>
    public sealed record ExtractionResult<T> where T : class
    {
        public T? Data { get; init; }

        public IReadOnlyList<ExtractionError> Errors { get; init; } = [];

        [MemberNotNullWhen(true, nameof(Data))]
        public bool IsValid => Data is not null && Errors.Count == 0;

        public static ExtractionResult<T> Success(T data) => new() { Data = data };

        public static ExtractionResult<T> Failure(ExtractionErrorCode code, string message) =>
            new() { Errors = [new ExtractionError(code, message)] };
    }
}
