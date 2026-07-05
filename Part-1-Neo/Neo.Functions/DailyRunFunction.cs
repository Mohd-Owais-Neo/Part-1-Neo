using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NEO.Core.Services;

namespace NEO.Functions
{
    public class DailyRunFunction
    {
        private readonly ILogger _logger;
        private readonly PipelineOrchestrator _orchestrator;

        public DailyRunFunction(
            ILoggerFactory loggerFactory,
            PipelineOrchestrator orchestrator)
        {
            _logger = loggerFactory.CreateLogger<DailyRunFunction>();
            _orchestrator = orchestrator;
        }

        // Monday-Friday at 8:00 AM IST (2:30 AM UTC)
        [Function("DailyRunFunction")]
        public async Task Run(
            [TimerTrigger("0 30 2 * * 1-5")] TimerInfo myTimer)
        {
            _logger.LogInformation("ProjectNEO DailyRunFunction started at: {time}", DateTime.Now);
            _logger.LogInformation("DailyRunFunction is the ONLY production scheduler for ProjectNEO.");

            try
            {
                await _orchestrator.RunDailyPipelineAsync();
                _logger.LogInformation("ProjectNEO DailyRunFunction completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError("ProjectNEO DailyRunFunction failed. FULL ERROR: {error}", ex.ToString());
                throw;
            }
        }
    }
}