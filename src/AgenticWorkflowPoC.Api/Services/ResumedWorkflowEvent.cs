using System;
using System.Threading.Channels;

namespace AgenticWorkflowPoC.Api.Services
{
    /// <summary>
    /// Immutable, tenant-aware resume signal (Chapter 3). It is a wake-up hint for the worker,
    /// never an authorization: the approval is already persisted in SQL before it is written,
    /// and the worker re-claims and re-evaluates the session from the database.
    /// </summary>
    public readonly record struct ResumedWorkflowEvent(string TenantId, Guid SessionId);

    public sealed class HitlWorkerOptions
    {
        public const string SectionName = "HitlWorker";

        /// <summary>Bounded capacity of the in-memory resume channel.</summary>
        public int ChannelCapacity { get; set; } = 1024;

        /// <summary>
        /// Fallback poll of the durable SQL queue: recovers signals lost to a crash, a cancelled
        /// request or a restart, and reclaims expired processing leases.
        /// </summary>
        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);
    }

    public static class ResumeSignalChannel
    {
        /// <summary>
        /// Unbounded channels are prohibited: with <see cref="BoundedChannelFullMode.Wait"/> producers
        /// slow down instead of dropping signals or exhausting memory.
        /// </summary>
        public static Channel<ResumedWorkflowEvent> Create(int capacity) =>
            Channel.CreateBounded<ResumedWorkflowEvent>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    }
}
