using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgenticWorkflowPoC.Api.Services
{
    public sealed class HitlSessionWorker : BackgroundService
    {
        private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<HitlSessionWorker> _logger;

        public HitlSessionWorker(IServiceScopeFactory scopeFactory, ILogger<HitlSessionWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<HitlWorkProcessor>();
                    if (await processor.ProcessNextAsync(stoppingToken))
                    {
                        continue;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "HITL background worker iteration failed.");
                }

                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }
}
