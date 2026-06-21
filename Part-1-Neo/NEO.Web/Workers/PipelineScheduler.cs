using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NEO.Web.Workers
{
    public class PipelineScheduler : BackgroundService
    {
        private readonly ILogger<PipelineScheduler> _logger;

        public PipelineScheduler(ILogger<PipelineScheduler> logger)
        {
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PipelineScheduler is disabled in NEO.Web.");
            _logger.LogInformation("Daily pipeline execution is handled only by Azure Functions (DailyRunFunction).");
            return Task.CompletedTask;
        }
    }
}