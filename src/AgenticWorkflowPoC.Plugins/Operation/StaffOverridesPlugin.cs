using System.Threading.Tasks;
using AgenticWorkflowPoC.Plugins;
using Microsoft.Extensions.Logging;

namespace AgenticWorkflowPoC.Plugins.Operations
{
    public enum StaffOverrideOutcome
    {
        Completed,
        RequiresApproval,
        InvalidInput
    }

    public sealed record StaffOverrideResult(StaffOverrideOutcome Outcome, string Message, string? Reason = null);

    public class StaffOverridesPlugin
    {
        private readonly ILogger<StaffOverridesPlugin> _logger;
        private readonly IHitlState _hitlState;

        // Staff Note: In a production scenario, you would inject ILogger, IFeatureManager, 
        // and domain services (e.g., IStaffRepository) via the constructor.

        private const string SuccessMessageFormat = "SUCCESS: Availability for {0} updated successfully without conflicts.";
        private const string HitlReasonFormat = "A shift conflict was detected for staff {0}.";
        private const string ErrorInvalidInput = "Staff ID is required.";

        public StaffOverridesPlugin( ILogger<StaffOverridesPlugin> logger, IHitlState hitlState )
        {
            _logger = logger;
            _hitlState = hitlState;
        }

        public async Task<StaffOverrideResult> OverrideAvailabilityAsync(
            string staffId,
            System.DateTimeOffset newAvailability,
            System.Threading.CancellationToken ct = default )
        {

            _logger.LogInformation("StaffOverridesPlugin executing for {StaffId} at {Time}", staffId, newAvailability);

            if( string.IsNullOrWhiteSpace(staffId) )
            {
                return new StaffOverrideResult(StaffOverrideOutcome.InvalidInput, ErrorInvalidInput);
            }

            // 1. Business Rule Evaluation
            // We encapsulate the logic within standard C# execution to avoid LLM hallucinations on critical rules.
            bool affectsExistingShift = await CheckIfConflictsWithExistingAsync( staffId, newAvailability, ct );

            if( affectsExistingShift )
            {
                var reason = string.Format(HitlReasonFormat, staffId);
                _hitlState.Suspend(reason);
                return new StaffOverrideResult(StaffOverrideOutcome.RequiresApproval, "Operation suspended pending human review.", reason);
            }

            // 3. Execute "Happy Path" if no human intervention is required
            await ExecuteOverrideAsync( staffId, newAvailability, ct );
            return new StaffOverrideResult(StaffOverrideOutcome.Completed, string.Format(SuccessMessageFormat, staffId));
        }

        public async Task<StaffOverrideResult> ExecuteApprovedAvailabilityAsync(
            string staffId,
            System.DateTimeOffset newAvailability,
            System.Threading.CancellationToken ct = default )
        {
            if( string.IsNullOrWhiteSpace(staffId) )
            {
                return new StaffOverrideResult(StaffOverrideOutcome.InvalidInput, ErrorInvalidInput);
            }

            await ExecuteOverrideAsync( staffId, newAvailability, ct );
            return new StaffOverrideResult(StaffOverrideOutcome.Completed, string.Format(SuccessMessageFormat, staffId));
        }

        // --- Simulated Domain Logic ---

        private Task<bool> CheckIfConflictsWithExistingAsync( string staffId, System.DateTimeOffset time, System.Threading.CancellationToken ct = default )
        {
            // Simulation: For testing purposes, any staffId starting with 'EMP-REQ' triggers the HITL flow.
            bool conflict = staffId.StartsWith( "EMP-REQ" );
            return Task.FromResult( conflict );
        }

        private Task ExecuteOverrideAsync( string staffId, System.DateTimeOffset time, System.Threading.CancellationToken ct = default )
        {
            // Logic to execute the SQL UPDATE or publish an event to Azure Service Bus / RabbitMQ
            return Task.CompletedTask;
        }
    }
}
