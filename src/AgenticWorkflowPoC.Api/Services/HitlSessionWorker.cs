using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticWorkflowPoC.Api.Services
{
    /// <summary>
    /// Drains approved HITL sessions. Woken immediately by <see cref="ResumedWorkflowEvent"/> signals,
    /// with a periodic sweep of the durable SQL queue as fallback.
    /// </summary>
    public sealed class HitlSessionWorker : BackgroundService
    {
        private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ChannelReader<ResumedWorkflowEvent> _signals;
        private readonly IOptions<HitlWorkerOptions> _options;
        private readonly ILogger<HitlSessionWorker> _logger;

        public HitlSessionWorker(
            IServiceScopeFactory scopeFactory,
            ChannelReader<ResumedWorkflowEvent> signals,
            IOptions<HitlWorkerOptions> options,
            ILogger<HitlSessionWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _signals = signals;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Drain everything claimable; each session is processed in its own DI scope.
                    while (await ProcessNextAsync(stoppingToken))
                    {
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "HITL background worker iteration failed.");
                    await Task.Delay(ErrorBackoff, stoppingToken);
                    continue;
                }

                await WaitForSignalOrSweepAsync(stoppingToken);
            }
        }

        private async Task<bool> ProcessNextAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<HitlWorkProcessor>();
            return await processor.ProcessNextAsync(stoppingToken);
        }

        private async Task WaitForSignalOrSweepAsync(CancellationToken stoppingToken)
        {
            using var sweep = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            sweep.CancelAfter(_options.Value.SweepInterval);

            try
            {
                if (await _signals.WaitToReadAsync(sweep.Token))
                {
                    while (_signals.TryRead(out var signal))
                    {
                        _logger.LogDebug(
                            "Resume signal received for session {SessionId} (tenant {TenantId}).",
                            signal.SessionId,
                            signal.TenantId);
                    }
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Sweep interval elapsed without signals: poll the durable queue.
            }
        }
    }
}
