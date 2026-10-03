using AgenticWorkflowPoC.Api.Services;
using System;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Services
{
    public class ExtractionValidatorTests
    {
        private readonly ExtractionValidator _validator = new();

        [Fact]
        public void Validate_WhenPayloadMatchesContract_ReturnsExtraction()
        {
            var result = _validator.Validate("{\"staffId\":\"EMP-102\",\"date\":\"2026-09-01T09:00:00Z\"}");

            Assert.True(result.IsValid);
            Assert.Equal("EMP-102", result.StaffId);
            Assert.Equal(DateTimeOffset.Parse("2026-09-01T09:00:00Z"), result.Date);
        }

        [Fact]
        public void Validate_WhenPayloadContainsUnknownProperty_RejectsIt()
        {
            var result = _validator.Validate("{\"staffId\":\"EMP-102\",\"date\":\"2026-09-01T09:00:00Z\",\"tool\":\"delete\"}");

            Assert.False(result.IsValid);
        }

        [Fact]
        public void Validate_WhenFieldHasWrongJsonType_RejectsIt()
        {
            var result = _validator.Validate("{\"staffId\":102,\"date\":\"2026-09-01T09:00:00Z\"}");

            Assert.False(result.IsValid);
        }

        [Fact]
        public void Validate_WhenDateIsInvalid_RejectsIt()
        {
            var result = _validator.Validate("{\"staffId\":\"EMP-102\",\"date\":\"not-a-date\"}");

            Assert.False(result.IsValid);
            Assert.Equal("invalid_date", result.Error);
        }
    }
}
