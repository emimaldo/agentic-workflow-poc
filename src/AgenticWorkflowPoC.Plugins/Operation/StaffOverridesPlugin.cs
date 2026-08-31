using System.ComponentModel;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace AgenticWorkflowPoC.Plugins.Operations
{
    public class StaffOverridesPlugin
    {
        private readonly ILogger<StaffOverridesPlugin> _logger;
        private readonly IHitlState _hitlState;

        // Staff Note: In a production scenario, you would inject ILogger, IFeatureManager, 
        // and domain services (e.g., IStaffRepository) via the constructor.

        public StaffOverridesPlugin( ILogger<StaffOverridesPlugin> logger, IHitlState hitlState )
        {
            _logger = logger;
            _hitlState = hitlState;
        }

        [KernelFunction( "OverrideStaffAvailability" )]
        [Description( "Overrides the availability schedule of a staff member. Requires human validation if the change affects an already assigned shift." )]
        public async Task<string> OverrideAvailabilityAsync(
            [Description( "The unique identifier of the staff member (e.g., EMP-102)" )] string staffId,
            [Description( "The new availability date and time in ISO 8601 format" )] string newAvailability )
        {
            _logger.LogInformation("ALERTA: StaffOverridesPlugin executing for {StaffId} at {Time}", staffId, newAvailability);

            // 1. Business Rule Evaluation
            // We encapsulate the logic within standard C# execution to avoid LLM hallucinations on critical rules.
            bool affectsExistingShift = await CheckIfConflictsWithExistingAsync( staffId, newAvailability );

            if( affectsExistingShift )
            {
                _hitlState.IsSuspended = true;
                _hitlState.Reason = $"A shift conflict was detected for staff {staffId}.";
                return "SYSTEM INSTRUCTION: STOP PROCESSING IMMEDIATELY. OPERATION SUSPENDED.";
            }

            // 3. Execute "Happy Path" if no human intervention is required
            await ExecuteOverrideAsync( staffId, newAvailability );
            return $"SUCCESS: Availability for {staffId} updated successfully without conflicts.";
        }

        // --- Simulated Domain Logic ---

        private Task<bool> CheckIfConflictsWithExistingAsync( string staffId, string time )
        {
            // Simulation: For testing purposes, any staffId starting with 'EMP-REQ' triggers the HITL flow.
            bool conflict = staffId.StartsWith( "EMP-REQ" );
            return Task.FromResult( conflict );
        }

        private Task ExecuteOverrideAsync( string staffId, string time )
        {
            // Logic to execute the SQL UPDATE or publish an event to Azure Service Bus / RabbitMQ
            return Task.CompletedTask;
        }
    }
}
