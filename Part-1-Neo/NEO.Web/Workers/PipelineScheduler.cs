using NEO.Core.Services;

namespace NEO.Web.Workers
{
    public class PipelineScheduler : BackgroundService
    {
        private readonly ILogger<PipelineScheduler> _logger;
        private readonly IConfiguration _config;

        // ── Schedule ──────────────────────────────
        private const int ScheduledHour = 6;   // 6 AM
        private const int ScheduledMinute = 0;
        // ─────────────────────────────────────────

        public PipelineScheduler(
            ILogger<PipelineScheduler> logger,
            IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "⏰ PipelineScheduler started → runs daily at {H}:{M:D2}",
                ScheduledHour, ScheduledMinute);

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var nextRun = GetNextRunTime(now);
                var waitTime = nextRun - now;

                _logger.LogInformation(
                    "⏳ Next pipeline run: {NextRun} (in {Hours}h {Minutes}m)",
                    nextRun.ToString("yyyy-MM-dd HH:mm"),
                    (int)waitTime.TotalHours,
                    waitTime.Minutes);

                // Wait until scheduled time
                await Task.Delay(waitTime, stoppingToken);

                if (stoppingToken.IsCancellationRequested) break;

                // Run the pipeline
                await RunPipelineAsync();
            }
        }

        private DateTime GetNextRunTime(DateTime now)
        {
            var todayRun = new DateTime(
                now.Year, now.Month, now.Day,
                ScheduledHour, ScheduledMinute, 0);

            // If today's run time already passed → schedule for tomorrow
            return now < todayRun ? todayRun : todayRun.AddDays(1);
        }

        private async Task RunPipelineAsync()
        {
            _logger.LogInformation(
                "🚀 Scheduled pipeline starting at {Time}",
                DateTime.Now.ToString("HH:mm:ss"));

            try
            {
                var connStr = _config.GetConnectionString("DefaultConnection")!;
                var apiKey = _config["AppSettings:AlphaVantageApiKey"]!;
                var smtpHost = _config["EmailSettings:SmtpHost"] ?? "smtp.gmail.com";
                var smtpPort = int.Parse(_config["EmailSettings:SmtpPort"] ?? "587");
                var fromEmail = _config["EmailSettings:FromEmail"] ?? "";
                var fromPassword = _config["EmailSettings:FromPassword"] ?? "";
                var toEmail = _config["EmailSettings:ToEmail"] ?? "";

                var orchestrator = new PipelineOrchestrator(
                    connStr, apiKey,
                    smtpHost, smtpPort,
                    fromEmail, fromPassword, toEmail);

                await orchestrator.RunDailyPipelineAsync();

                _logger.LogInformation(
                    "✅ Scheduled pipeline completed at {Time}",
                    DateTime.Now.ToString("HH:mm:ss"));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "❌ Scheduled pipeline failed: {Error}", ex.Message);
            }
        }
    }
}

