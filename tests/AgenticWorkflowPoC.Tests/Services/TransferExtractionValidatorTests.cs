using System;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Development;
using AgenticWorkflowPoC.Api.Services;
using AgenticWorkflowPoC.Tests.Support;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Services
{
    public class TransferExtractionValidatorTests
    {
        [Fact]
        public async Task ExtractAndValidate_ValidPayload_EnrichesWithTenantContext()
        {
            var scenario = new TransferScenario(new FakeChatClient(
                "{\"destinationAccount\":\"de89 3704 0044 0532 0130 00\",\"amount\":250.00,\"currency\":\"eur\"}"));

            var result = await scenario.CreateValidator().ExtractAndValidateAsync(FakeChatClient.LowValueIntent);

            Assert.True(result.IsValid);
            Assert.Equal(FakeChatClient.DestinationIban, result.Data.DestinationAccount);
            Assert.Equal(250.00m, result.Data.Amount);
            Assert.Equal("EUR", result.Data.Currency);
            Assert.Equal(TransferScenario.TenantA, result.Data.TenantId);
            Assert.Equal(TransferScenario.OperatingAccount, result.Data.SourceAccountId);
        }

        [Fact]
        public async Task ExtractAndValidate_EnforcesInferenceContract()
        {
            var scenario = new TransferScenario();

            await scenario.CreateValidator().ExtractAndValidateAsync(FakeChatClient.LowValueIntent);

            var options = scenario.Chat.LastOptions;
            Assert.NotNull(options);
            var format = Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
            Assert.NotNull(format.Schema);
            Assert.Equal("transfer_request_schema", format.SchemaName);
            Assert.Equal(0f, options.Temperature);
            Assert.IsType<NoneChatToolMode>(options.ToolMode);
            Assert.True(options.Tools is null || options.Tools.Count == 0);
        }

        [Theory]
        [InlineData("{\"destinationAccount\":\"DE89370400440532013000\",\"amount\":250.00,\"currency\":\"EUR\",\"tenantId\":\"tenant-b\"}")]
        [InlineData("{\"destinationAccount\":\"DE89370400440532013000\",\"amount\":250.00,\"currency\":\"EUR\",\"sourceAccountId\":\"OPS-EUR-999\"}")]
        [InlineData("{\"destinationAccount\":\"DE89370400440532013000\",\"amount\":\"250.00\",\"currency\":\"EUR\"}")]
        [InlineData("{\"destinationAccount\":\"DE89370400440532013000\",\"currency\":\"EUR\"}")]
        [InlineData("not json at all")]
        public async Task ExtractAndValidate_SchemaViolations_AreRejected(string modelOutput)
        {
            var scenario = new TransferScenario(new FakeChatClient(modelOutput));

            var result = await scenario.CreateValidator().ExtractAndValidateAsync(FakeChatClient.LowValueIntent);

            Assert.False(result.IsValid);
            Assert.Equal(ExtractionErrorCode.SchemaViolation, Assert.Single(result.Errors).Code);
        }

        [Fact]
        public async Task ExtractAndValidate_NullField_IsMissingField()
        {
            var scenario = new TransferScenario(new FakeChatClient(
                "{\"destinationAccount\":null,\"amount\":250.00,\"currency\":\"EUR\"}"));

            var result = await scenario.CreateValidator().ExtractAndValidateAsync(FakeChatClient.LowValueIntent);

            Assert.Equal(ExtractionErrorCode.MissingField, Assert.Single(result.Errors).Code);
        }

        [Fact]
        public async Task ExtractAndValidate_EmptyResponse_IsRejected()
        {
            var scenario = new TransferScenario(new FakeChatClient("   "));

            var result = await scenario.CreateValidator().ExtractAndValidateAsync(FakeChatClient.LowValueIntent);

            Assert.Equal(ExtractionErrorCode.EmptyResponse, Assert.Single(result.Errors).Code);
        }

        [Fact]
        public async Task ExtractAndValidate_MarkdownFencedJson_IsAccepted()
        {
            var scenario = new TransferScenario(new FakeChatClient($"```json\n{FakeChatClient.LowValueJson}\n```"));

            var result = await scenario.CreateValidator().ExtractAndValidateAsync(FakeChatClient.LowValueIntent);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ExtractAndValidate_TenantWithoutPolicy_FailsClosed()
        {
            var scenario = new TransferScenario(tenantId: "tenant-unknown");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => scenario.CreateValidator().ExtractAndValidateAsync(FakeChatClient.LowValueIntent));
        }
    }
}
