using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Plugins.Operations;

namespace AgenticWorkflowPoC.Api.Development
{
    public sealed class InMemoryStaffAvailabilityExecutor : IStaffAvailabilityExecutor
    {
        private readonly ConcurrentDictionary<Guid, (string TenantId, string StaffId, DateTimeOffset Availability)> _operations = new();

        public int ExecutionCount => _operations.Count;

        public Task ExecuteAsync(
            Guid operationId,
            string tenantId,
            string staffId,
            DateTimeOffset newAvailability,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var operation = (tenantId, staffId, newAvailability);
            if (_operations.TryAdd(operationId, operation))
            {
                return Task.CompletedTask;
            }

            if (_operations.TryGetValue(operationId, out var existing) && existing != operation)
            {
                throw new InvalidOperationException("An idempotency key was reused for a different availability operation.");
            }

            return Task.CompletedTask;
        }
    }
}
