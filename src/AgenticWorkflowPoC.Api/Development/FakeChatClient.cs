using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace AgenticWorkflowPoC.Api.Development
{
    /// <summary>
    /// Deterministic, zero-token <see cref="IChatClient"/> for local runs and tests.
    /// Records the options it received so tests can assert the inference contract.
    /// </summary>
    public sealed class FakeChatClient : IChatClient
    {
        public const string DestinationIban = "DE89370400440532013000";

        public const string HighValueJson =
            "{\"destinationAccount\":\"DE89370400440532013000\",\"amount\":12500.00,\"currency\":\"EUR\"}";

        public const string LowValueJson =
            "{\"destinationAccount\":\"DE89370400440532013000\",\"amount\":250.00,\"currency\":\"EUR\"}";

        /// <summary>User intents whose literals match the canned responses (needed for grounding).</summary>
        public const string HighValueIntent = "Transfer 12500.00 EUR to DE89 3704 0044 0532 0130 00";
        public const string LowValueIntent = "Transfer 250.00 EUR to DE89 3704 0044 0532 0130 00";

        private readonly string _response;
        private int _callCount;

        public FakeChatClient(string response)
        {
            _response = response;
        }

        public static FakeChatClient HighValueTransfer() => new(HighValueJson);

        public static FakeChatClient LowValueTransfer() => new(LowValueJson);

        public int CallCount => Volatile.Read(ref _callCount);

        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            LastOptions = options;

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _response)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
