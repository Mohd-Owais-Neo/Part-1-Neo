using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NEO.Core.Services;
using System;

namespace NEO.Functions
{
    public class DailyRunFunction
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;

        public DailyRunFunction(
            ILoggerFactory loggerFactory,
            IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<DailyRunFunction>();
            _configuration = configuration;
        }

        // Runs Monday-Friday at 8:00 AM IST (2:30 AM UTC)
        [Function("DailyRunFunction")]
        public async Task Run(
            [TimerTrigger("0 30 2 * * 1-5")] TimerInfo myTimer)
        {
            _logger.LogInformation(
                "ProjectNEO Daily Run started at: {time}",
                DateTime.Now);

            try
            {
                var connStr = _configuration
                    .GetConnectionString("DefaultConnection")
                    ?? throw new Exception("Connection string not found!");

                var apiKey = _configuration["AppSettings:AlphaVantageApiKey"]
                    ?? throw new Exception("API Key not found!");

                var smtpHost = _configuration["EmailSettings:SmtpHost"] ?? "smtp.gmail.com";
                var smtpPort = int.Parse(_configuration["EmailSettings:SmtpPort"] ?? "587");
                var fromEmail = _configuration["EmailSettings:FromEmail"] ?? "";
                var fromPassword = _configuration["EmailSettings:FromPassword"] ?? "";
                var toEmail = _configuration["EmailSettings:ToEmail"] ?? "";

                var orchestrator = new PipelineOrchestrator(
                    connStr, apiKey,
                    smtpHost, smtpPort,
                    fromEmail, fromPassword, toEmail);
                await orchestrator.RunDailyPipelineAsync();

                _logger.LogInformation("ProjectNEO Daily Run completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "ProjectNEO Daily Run failed: {error}",
                    ex.Message);
                throw;
            }
        }
    }
}