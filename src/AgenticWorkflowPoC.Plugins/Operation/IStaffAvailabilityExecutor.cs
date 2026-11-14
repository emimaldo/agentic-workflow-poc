using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgenticWorkflowPoC.Plugins.Operations
{
    public interface IStaffAvailabilityExecutor
    {
        Task ExecuteAsync(
            Guid operationId,
            string tenantId,
            string staffId,
            DateTimeOffset newAvailability,
            CancellationToken cancellationToken = default);
    }
}
