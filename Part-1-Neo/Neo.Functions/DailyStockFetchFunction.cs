using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NEO.Core.Data;
using NEO.Core.Services;

namespace NEO.Functions
{
    public class DailyStockFetchFunction
    {
        private readonly ILogger _logger;
        private readonly MarketDataService _marketData;
        private readonly DatabaseHelper _db;
        private readonly EmailAlertService _emailAlert;

        public DailyStockFetchFunction(
            ILoggerFactory loggerFactory,
            MarketDataService marketData,
            DatabaseHelper db,
            EmailAlertService emailAlert)
        {
            _logger = loggerFactory.CreateLogger<DailyStockFetchFunction>();
            _marketData = marketData;
            _db = db;
            _emailAlert = emailAlert;
        }

        // Monday-Friday at 6:00 AM IST (00:30 AM UTC)
        [Function("DailyStockFetchFunction")]
        public async Task Run(
            [TimerTrigger("0 30 0 * * 1-5")] TimerInfo myTimer)
        {
            var runId = $"FETCH-{DateTime.Now:yyyyMMdd-HHmmss}";
            var businessDate = DateTime.Today;

            _logger.LogInformation("ProjectNEO DailyStockFetchFunction started at: {time}", DateTime.Now);
            _logger.LogInformation("Run ID: {runId}, Business Date: {businessDate}", runId, businessDate.ToString("yyyy-MM-dd"));

            try
            {
                var dbOk = await _db.TestConnectionAsync();

                if (!dbOk)
                    throw new Exception("Database connection failed during stock fetch.");

                //var stocks = await _marketData.GetStocksFromYahooAsync();
                var stocks = await _marketData.GetStocksFromBhavcopyAsync(maxCount: 500);

                if (stocks.Count < 100)
                {
                    throw new Exception(
                        $"MarketDataService returned only {stocks.Count} stocks. " +
                        "Expected at least 100. This means NSE/Yahoo fetch failed and fallback data was used.");
                }

                await _db.InsertAllStocksAsync(runId, businessDate, stocks);

                await _db.InsertRunLogAsync(new NEO.Core.Models.RunLog
                {
                    RunId = runId,
                    BusinessDate = businessDate,
                    Stage = "STOCK_FETCH",
                    Message = $"Stock fetch completed. Stocks inserted: {stocks.Count}",
                    Status = "SUCCESS"
                });

                await _emailAlert.SendStockFetchSummaryAsync(
                    runId,
                    businessDate,
                    stocks,
                    "SUCCESS",
                    $"Stock fetch completed successfully. Stocks inserted: {stocks.Count}. If count is below 100, external NSE/Yahoo fetch likely failed");

                _logger.LogInformation("ProjectNEO DailyStockFetchFunction completed successfully. Stocks inserted: {count}", stocks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError("ProjectNEO DailyStockFetchFunction failed. FULL ERROR: {error}", ex.ToString());

                try
                {
                    await _db.InsertRunLogAsync(new NEO.Core.Models.RunLog
                    {
                        RunId = runId,
                        BusinessDate = businessDate,
                        Stage = "STOCK_FETCH",
                        Message = $"Stock fetch failed: {ex.Message}",
                        Status = "FAILED"
                    });
                }
                catch
                {
                    // If DB logging itself fails, do not hide the original failure.
                }

                try
                {
                    await _emailAlert.SendStockFetchSummaryAsync(
                        runId,
                        businessDate,
                        new List<NEO.Core.Models.Stock>(),
                        "FAILED",
                        ex.Message);
                }
                catch
                {
                    // If failure email also fails, keep original function failure visible.
                }

                throw;
            }
        }
    }
}