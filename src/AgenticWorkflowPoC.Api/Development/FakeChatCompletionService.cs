using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AgenticWorkflowPoC.Api.Development
{
    // Simple deterministic fake for local development/testing when Ollama is not available.
    public class FakeChatCompletionService : IChatCompletionService
    {
        private readonly bool _returnConflict;
        private readonly string? _resumeContent;

        public FakeChatCompletionService(bool returnConflict = true, string? resumeContent = null)
        {
            _returnConflict = returnConflict;
            _resumeContent = resumeContent;
        }

        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
        {
            var content = new ChatMessageContent();

            // If a resume override was provided, return it (used in tests).
            if (!string.IsNullOrEmpty(_resumeContent))
            {
                content.Content = _resumeContent;
            }
            else if (_returnConflict)
            {
                content.Content = "{\"staffId\": \"EMP-REQ-001\", \"date\": \"2026-09-01T09:00:00Z\"}";
            }
            else
            {
                content.Content = "{\"staffId\": \"EMP-102\", \"date\": \"2026-09-01T09:00:00Z\"}";
            }

            IReadOnlyList<ChatMessageContent> list = new List<ChatMessageContent> { content };
            return Task.FromResult(list);
        }

        public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
        {
            return StreamImpl();

            async IAsyncEnumerable<StreamingChatMessageContent> StreamImpl()
            {
                await Task.Yield();
                yield break;
            }
        }
    }
}
