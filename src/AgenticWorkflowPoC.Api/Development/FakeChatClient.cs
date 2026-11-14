using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace AgenticWorkflowPoC.Api.Development
{
    public sealed class FakeChatClient : IChatClient
    {
        private readonly bool _returnConflict;
        private readonly string? _responseOverride;

        public FakeChatClient(bool returnConflict = true)
        {
            _returnConflict = returnConflict;
        }

        public FakeChatClient(string response)
        {
            _responseOverride = response;
        }

        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            var content = _responseOverride ?? (_returnConflict
                ? "{\"staffId\": \"EMP-REQ-001\", \"date\": \"2026-09-01T09:00:00Z\"}"
                : "{\"staffId\": \"EMP-102\", \"date\": \"2026-09-01T09:00:00Z\"}");

            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, content)));
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
