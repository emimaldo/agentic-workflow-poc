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

        // ---- Layer 2: domain invariants ------------------------------------------------

        [Theory]
        [InlineData("DE89370400440532013001", "250.00", "EUR", ExtractionErrorCode.InvalidAccount)]   // bad mod-97 checksum
        [InlineData("NOT-AN-IBAN", "250.00", "EUR", ExtractionErrorCode.InvalidAccount)]
        [InlineData("DE89370400440532013000", "0", "EUR", ExtractionErrorCode.InvalidAmount)]
        [InlineData("DE89370400440532013000", "-250.00", "EUR", ExtractionErrorCode.InvalidAmount)]
        [InlineData("DE89370400440532013000", "250.001", "EUR", ExtractionErrorCode.InvalidAmount)]   // EUR has 2 minor units
        [InlineData("DE89370400440532013000", "250.5", "JPY", ExtractionErrorCode.InvalidAmount)]     // JPY has 0 minor units
        [InlineData("DE89370400440532013000", "250.00", "XYZ", ExtractionErrorCode.InvalidCurrency)]
        public async Task ExtractAndValidate_InvariantViolations_AreRejected(
            string account, string amount, string currency, ExtractionErrorCode expected)
        {
            var json = $"{{\"destinationAccount\":\"{account}\",\"amount\":{amount},\"currency\":\"{currency}\"}}";
            var scenario = new TransferScenario(new FakeChatClient(json));

            var result = await scenario.CreateValidator().ExtractAndValidateAsync($"Transfer {amount} {currency} to {account}");

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == expected);
        }

        // ---- Layer 3: grounding -------------------------------------------------------

        [Fact]
        public async Task ExtractAndValidate_HallucinatedAccount_IsUngrounded()
        {
            // A valid IBAN that the user never wrote.
            var scenario = new TransferScenario(new FakeChatClient(
                "{\"destinationAccount\":\"GB29NWBK60161331926819\",\"amount\":250.00,\"currency\":\"EUR\"}"));

            var result = await scenario.CreateValidator().ExtractAndValidateAsync(FakeChatClient.LowValueIntent);

            Assert.Equal(ExtractionErrorCode.Ungrounded, Assert.Single(result.Errors).Code);
        }

        [Fact]
        public async Task ExtractAndValidate_HallucinatedAmount_IsUngrounded()
        {
            var scenario = new TransferScenario(FakeChatClient.LowValueTransfer());

            var result = await scenario.CreateValidator().ExtractAndValidateAsync(
                "Transfer two hundred and fifty euros to DE89 3704 0044 0532 0130 00");

            Assert.Equal(ExtractionErrorCode.Ungrounded, Assert.Single(result.Errors).Code);
        }

        [Theory]
        [InlineData("Transferí 12.500 EUR a DE89 3704 0044 0532 0130 00")]
        [InlineData("Transfer 12,500 EUR to DE89 3704 0044 0532 0130 00")]
        public async Task ExtractAndValidate_AmbiguousSeparator_IsRejectedInsteadOfGuessed(string intent)
        {
            var scenario = new TransferScenario(FakeChatClient.HighValueTransfer());

            var result = await scenario.CreateValidator().ExtractAndValidateAsync(intent);

            Assert.Equal(ExtractionErrorCode.AmbiguousAmount, Assert.Single(result.Errors).Code);
        }

        [Theory]
        [InlineData("Transferí 12.500,00 EUR a DE89 3704 0044 0532 0130 00")]
        [InlineData("Transfer 12,500.00 EUR to DE89 3704 0044 0532 0130 00")]
        [InlineData("Transfer 12500 EUR to DE89370400440532013000")]
        [InlineData("Transfer 12 500 EUR to de89 3704 0044 0532 0130 00")]
        public async Task ExtractAndValidate_UnambiguousAmountFormats_AreGrounded(string intent)
        {
            var scenario = new TransferScenario(FakeChatClient.HighValueTransfer());

            var result = await scenario.CreateValidator().ExtractAndValidateAsync(intent);

            Assert.True(result.IsValid);
            Assert.Equal(12_500m, result.Data.Amount);
        }

        // ---- Layer 4: tenant limits -----------------------------------------------------

        [Fact]
        public async Task ExtractAndValidate_CurrencyNotEnabledForTenant_IsRejected()
        {
            var scenario = new TransferScenario(new FakeChatClient(
                "{\"destinationAccount\":\"DE89370400440532013000\",\"amount\":250.00,\"currency\":\"GBP\"}"));

            var result = await scenario.CreateValidator().ExtractAndValidateAsync(
                "Transfer 250.00 GBP to DE89 3704 0044 0532 0130 00");

            Assert.Equal(ExtractionErrorCode.TenantLimitExceeded, Assert.Single(result.Errors).Code);
        }

        [Fact]
        public async Task ExtractAndValidate_AbovePerTransferLimit_IsRejected()
        {
            var scenario = new TransferScenario(FakeChatClient.HighValueTransfer());
            scenario.PolicyA.MaxPerTransfer = 5_000m;

            var result = await scenario.CreateValidator().ExtractAndValidateAsync(FakeChatClient.HighValueIntent);

            Assert.Equal(ExtractionErrorCode.TenantLimitExceeded, Assert.Single(result.Errors).Code);
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
