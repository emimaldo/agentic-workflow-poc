using System.Threading.Tasks;
using System;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Operations;
using AgenticWorkflowPoC.Api.Development;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Plugins.Operations
{
    public class StaffOverridesPluginTests
    {
        // Staff Note: In a real scenario with injected dependencies, 
        // you would setup your mocks here in the constructor.
        // private readonly Mock<IFeatureManager> _featureManagerMock;
        // private readonly StaffOverridesPlugin _sut; (System Under Test)

        [Fact]
        public async Task OverrideAvailability_WhenNoConflict_ReturnsSuccessMessage()
        {
            // Arrange
            var hitl = new HitlStateService();
            var plugin = CreatePlugin(hitl);
            string staffId = "EMP-102"; // Regular ID, no conflict
            var newAvailability = DateTimeOffset.Parse("2026-09-01T09:00:00Z");

            // Act
            var result = await plugin.OverrideAvailabilityAsync(Guid.NewGuid(), "tenant-a", staffId, newAvailability);

            // Assert
            Assert.Equal(StaffOverrideOutcome.Completed, result.Outcome);
            Assert.Contains( staffId, result.Message );
        }

        [Fact]
        public async Task OverrideAvailability_WhenConflictDetected_SetsAsyncLocalSignalAndReturnsSuspensionMessage()
        {
            // Arrange
            var hitl = new HitlStateService();
            var plugin = CreatePlugin(hitl);
            string staffId = "EMP-REQ-001"; // ID prefix that triggers the simulated conflict
            var newAvailability = DateTimeOffset.Parse("2026-09-01T09:00:00Z");

            // Act
            var result = await plugin.OverrideAvailabilityAsync(Guid.NewGuid(), "tenant-a", staffId, newAvailability);

            // Assert
            Assert.Equal(StaffOverrideOutcome.RequiresApproval, result.Outcome);
            Assert.True( hitl.IsSuspended );
            Assert.Contains( staffId, hitl.Reason );
        }

        [Fact]
        public async Task ExecuteApprovedAvailability_DoesNotReopenReview()
        {
            var hitl = new HitlStateService();
            var plugin = CreatePlugin(hitl);

            var result = await plugin.ExecuteApprovedAvailabilityAsync(
                Guid.NewGuid(),
                "tenant-a",
                "EMP-REQ-001",
                DateTimeOffset.Parse("2026-09-01T09:00:00Z"));

            Assert.Equal(StaffOverrideOutcome.Completed, result.Outcome);
            Assert.False(hitl.IsSuspended);
        }

        private static StaffOverridesPlugin CreatePlugin(IHitlState hitl) =>
            new(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<StaffOverridesPlugin>.Instance,
                hitl,
                new InMemoryStaffAvailabilityExecutor());
    }
}
