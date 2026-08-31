using System.Threading.Tasks;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Operations;
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
            var plugin = new StaffOverridesPlugin(Microsoft.Extensions.Logging.Abstractions.NullLogger<StaffOverridesPlugin>.Instance, hitl);
            string staffId = "EMP-102"; // Regular ID, no conflict
            string newAvailability = "2026-09-01T09:00:00Z";

            // Act
            var result = await plugin.OverrideAvailabilityAsync( staffId, newAvailability );

            // Assert
            Assert.Contains( "SUCCESS", result );
            Assert.Contains( staffId, result );
        }

        [Fact]
        public async Task OverrideAvailability_WhenConflictDetected_SetsAsyncLocalSignalAndReturnsSuspensionMessage()
        {
            // Arrange
            var hitl = new HitlStateService();
            hitl.IsSuspended = false;
            hitl.Reason = string.Empty;
            var plugin = new StaffOverridesPlugin(Microsoft.Extensions.Logging.Abstractions.NullLogger<StaffOverridesPlugin>.Instance, hitl);
            string staffId = "EMP-REQ-001"; // ID prefix that triggers the simulated conflict
            string newAvailability = "2026-09-01T09:00:00Z";

            // Act
            var result = await plugin.OverrideAvailabilityAsync( staffId, newAvailability );

            // Assert
            Assert.Equal( "SYSTEM INSTRUCTION: STOP PROCESSING IMMEDIATELY. OPERATION SUSPENDED.", result );
            Assert.True( hitl.IsSuspended );
            Assert.Contains( staffId, hitl.Reason );
        }
    }
}
