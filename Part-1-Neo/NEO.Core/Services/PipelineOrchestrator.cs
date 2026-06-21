using Microsoft.IdentityModel.Protocols.Configuration;
using NEO.Core.Data;
using NEO.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Services
{
    public class PipelineOrchestrator
    {
        private readonly DatabaseHelper _db;
        private readonly ApiDataService _api;
        private readonly MarketDataService _marketData;
        private readonly IntersectionService _intersection;
        private readonly StockFilterService _stockFilter;
        private readonly TopStockSelectorService _stockSelector;
        private readonly RiskManagementService _riskManager;
        private readonly EmailAlertService _emailAlert;

        public PipelineOrchestrator(
            DatabaseHelper db,
            ApiDataService api,
            MarketDataService marketData,
            IntersectionService intersection,
            StockFilterService stockFilter,
            TopStockSelectorService stockSelector,
            RiskManagementService riskManager,
            EmailAlertService emailAlert)
        {
            _db = db;
            _api = api;
            _marketData = marketData;
            _intersection = intersection;
            _stockFilter = stockFilter;
            _stockSelector = stockSelector;
            _riskManager = riskManager;
            _emailAlert = emailAlert;
        }

        // =============================================
        // MASTER RUN — Called by Azure Function / manual host
        // =============================================
        public async Task RunDailyPipelineAsync()
        {
            var runId = $"RUN-{DateTime.Now:yyyyMMdd-HHmmss}";
            var businessDate = DateTime.Today;

            Console.WriteLine("==============================================");
            Console.WriteLine("PROJECT NEO - Daily Pipeline");
            Console.WriteLine($"Run ID    : {runId}");
            Console.WriteLine($"Date      : {businessDate:yyyy-MM-dd}");
            Console.WriteLine("==============================================");

            try
            {
                await Stage0_TestConnections(runId, businessDate);
                await Stage0_5_LoadMainStockData(runId, businessDate);

                var usSectors = await Stage1_FetchSectors(
                    runId, businessDate, "US", "Table_2_Sector_1D_US");

                var indiaSectors = await Stage1_FetchSectors(
                    runId, businessDate, "INDIA", "Table_2_Sector_1D_India");

                var chinaSectors = await Stage1_FetchSectors(
                    runId, businessDate, "CHINA", "Table_2_Sector_1D_China");

                var selectedSectors = await Stage4_IntersectionLogic(
                    runId, businessDate, usSectors, indiaSectors, chinaSectors);

                var topStocks = await Stage5_SelectTopStocks(
                    runId, businessDate, selectedSectors);

                var signals = await Stage8_RiskManagement(
                    runId, businessDate, topStocks);

                await Stage9_SendEmail(
                    runId, businessDate, selectedSectors, signals);

                await _db.InsertRunLogAsync(new RunLog
                {
                    RunId = runId,
                    BusinessDate = businessDate,
                    Stage = "PIPELINE",
                    Message = "Daily pipeline completed successfully",
                    Status = "SUCCESS"
                });

                Console.WriteLine();
                Console.WriteLine("==============================================");
                Console.WriteLine("✅ PIPELINE COMPLETE!");
                Console.WriteLine("==============================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"❌ PIPELINE FAILED: {ex.Message}");
                Console.WriteLine($"   Stack: {ex.StackTrace}");

                await _db.InsertRunLogAsync(new RunLog
                {
                    RunId = runId,
                    BusinessDate = businessDate,
                    Stage = "PIPELINE",
                    Message = $"Pipeline failed: {ex.Message}",
                    Status = "FAILED"
                });
            }
        }

        // =============================================
        // STAGE 0 — Test All Connections
        // =============================================
        private async Task Stage0_TestConnections(string runId, DateTime businessDate)
        {
            Console.WriteLine("\n🔵 STAGE 0 - Testing Connections...");

            var dbOk = await _db.TestConnectionAsync();
            if (!dbOk)
                throw new Exception("Database connection failed!");

            Console.WriteLine("   ✅ Database connected");

            var apiOk = await _api.TestApiConnectionAsync();
            if (!apiOk)
                throw new Exception("Alpha Vantage API connection failed!");

            Console.WriteLine("   ✅ Alpha Vantage API connected");

            Console.WriteLine("   → Waiting 15 seconds for API rate limit reset...");
            await Task.Delay(15000);

            await _db.InsertRunLogAsync(new RunLog
            {
                RunId = runId,
                BusinessDate = businessDate,
                Stage = "STAGE_0",
                Message = "All connections verified",
                Status = "SUCCESS"
            });

            Console.WriteLine("✅ STAGE 0 COMPLETE");
        }

        // =============================================
        // STAGE 0.5 — Load main stock data
        // =============================================
        private async Task Stage0_5_LoadMainStockData(string runId, DateTime businessDate)
        {
            Console.WriteLine("\n🔵 STAGE 0.5 - Loading Main Stock Data from Yahoo...");

            var hasData = await _db.HasTodaysDataAsync("Table_1_All_Stocks", businessDate);

            if (hasData)
            {
                Console.WriteLine("   ✅ Stock data already exists for today - skipping Yahoo fetch");
                return;
            }

            var stocks = await _marketData.GetStocksFromYahooAsync();

            if (stocks.Count == 0)
            {
                Console.WriteLine("   ⚠️ Yahoo returned no stock data");
                return;
            }

            await _db.InsertAllStocksAsync(runId, businessDate, stocks);

            Console.WriteLine($"   ✅ Saved {stocks.Count} stocks to Table_1_All_Stocks");

            await _db.InsertRunLogAsync(new RunLog
            {
                RunId = runId,
                BusinessDate = businessDate,
                Stage = "STAGE_0_5",
                Message = $"Yahoo stock data loaded into Table_1_All_Stocks: {stocks.Count}",
                Status = "SUCCESS"
            });
        }

        // =============================================
        // STAGE 1 / 2 / 3 — Fetch sector rankings
        // =============================================
        private async Task<List<Sector>> Stage1_FetchSectors(
            string runId,
            DateTime businessDate,
            string market,
            string tableName)
        {
            Console.WriteLine($"\n🔵 Fetching {market} Sector Rankings...");

            var hasData = await _db.HasTodaysDataAsync(tableName, businessDate);

            if (hasData)
            {
                Console.WriteLine($"   ✅ Cache hit! {market} data exists for today");
                Console.WriteLine("   → Loading from DB instead of API...");
                var cached = await _db.LoadSectorsFromDbAsync(tableName);
                Console.WriteLine($"   ✅ Loaded {cached.Count} sectors from cache");
                return cached;
            }

            Console.WriteLine("   → No cache found - calling API...");

            var sectors = await _api.FetchSectorPerformanceAsync(market);

            if (sectors.Count == 0)
            {
                Console.WriteLine($"   ⚠️ No sectors returned for {market}");

                await _db.InsertRunLogAsync(new RunLog
                {
                    RunId = runId,
                    BusinessDate = businessDate,
                    Stage = $"SECTORS_{market}",
                    Message = "No sectors returned from API",
                    Status = "WARNING"
                });

                return sectors;
            }

            // KEEP THIS RULE ACTIVE IN CURRENT VERSION:
            // Forbidden sectors must still be excluded.
            var forbidden = await _db.GetForbiddenSectorsAsync();

            var filtered = sectors
                .Where(s => !forbidden.Contains(s.SectorName.ToLower()))
                .ToList();

            for (int i = 0; i < filtered.Count; i++)
                filtered[i].Rank = i + 1;

            Console.WriteLine($"   → {sectors.Count} sectors fetched");
            Console.WriteLine($"   → {filtered.Count} sectors after filtering");

            await _db.EnsureTableExistsAsync(tableName);
            await _db.InsertSectorRankingAsync(tableName, runId, businessDate, filtered);

            Console.WriteLine($"   ✅ Saved to {tableName}");

            Console.WriteLine($"\n   📊 {market} Sector Rankings:");
            foreach (var s in filtered.Take(5))
            {
                Console.WriteLine($"   Rank {s.Rank}: {s.SectorName} → {s.PctChange}%");
            }

            await _db.InsertRunLogAsync(new RunLog
            {
                RunId = runId,
                BusinessDate = businessDate,
                Stage = $"SECTORS_{market}",
                Message = $"{market} sectors saved: {filtered.Count}",
                Status = "SUCCESS"
            });

            Console.WriteLine($"✅ {market} SECTORS COMPLETE");
            return filtered;
        }

        // =============================================
        // STAGE 4 — Intersection logic
        // =============================================
        private async Task<List<string>> Stage4_IntersectionLogic(
            string runId,
            DateTime businessDate,
            List<Sector> usSectors,
            List<Sector> indiaSectors,
            List<Sector> chinaSectors)
        {
            Console.WriteLine("\n🔵 STAGE 4 - Intersection Logic...");

            var scores = _intersection.ScoreSectors(usSectors, indiaSectors, chinaSectors);

            var selectedSectors = _intersection.FindIntersectingSectors(
                usSectors, indiaSectors, chinaSectors, topN: 5);

            Console.WriteLine("\n   🎯 SELECTED SECTORS FOR INVESTMENT:");
            for (int i = 0; i < selectedSectors.Count; i++)
            {
                Console.WriteLine($"   {i + 1}. {selectedSectors[i]}");
            }

            await _db.EnsureTableExistsAsync("Table_3_Sector_Intersection");
            await _db.InsertIntersectionResultAsync(
                runId, businessDate, selectedSectors, scores);

            await _db.InsertRunLogAsync(new RunLog
            {
                RunId = runId,
                BusinessDate = businessDate,
                Stage = "STAGE_4",
                Message = $"Intersection complete. Selected: {string.Join(", ", selectedSectors)}",
                Status = "SUCCESS"
            });

            Console.WriteLine("✅ STAGE 4 COMPLETE");
            return selectedSectors;
        }

        // =============================================
        // STAGE 5 — Top stock selection
        // =============================================
        private async Task<List<Stock>> Stage5_SelectTopStocks(
            string runId,
            DateTime businessDate,
            List<string> selectedSectors)
        {
            Console.WriteLine("\n🔵 STAGE 5 - Top Stock Selection...");

            if (selectedSectors.Count == 0)
            {
                Console.WriteLine("   ⚠️ No sectors selected - skipping");
                return new List<Stock>();
            }

            var cached = await _db.LoadTopStocksAsync(businessDate);
            var cachedSectors = cached
                .Select(s => s.SectorName)
                .Distinct()
                .ToList();

            bool cacheValid = cached.Count > 0 &&
                              selectedSectors.All(s =>
                                  cachedSectors.Any(cs =>
                                      cs.Equals(s, StringComparison.OrdinalIgnoreCase)));

            if (cacheValid)
            {
                Console.WriteLine("   ✅ Cache hit! Sectors match today's selection");
                Console.WriteLine($"   → Loaded {cached.Count} stocks from DB");
                return cached;
            }

            if (cached.Count > 0 && !cacheValid)
            {
                Console.WriteLine("   ⚠️ Cache INVALID - sectors changed, selecting fresh data");
                Console.WriteLine($"   → Cached:   {string.Join(", ", cachedSectors)}");
                Console.WriteLine($"   → Required: {string.Join(", ", selectedSectors)}");
            }
            else
            {
                Console.WriteLine("   → No cache found - loading from Table_1_All_Stocks...");
            }

            List<Stock> topStocks;

            try
            {
                topStocks = await _stockSelector.SelectTopStocksAsync(selectedSectors, topN: 10);

                if (topStocks.Count == 0)
                {
                    Console.WriteLine("   ⚠️ DB returned no usable stock data");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ DB mode error: {ex.Message}");
                topStocks = new List<Stock>();
            }

            Console.WriteLine($"\n   📊 Top {topStocks.Count} stocks selected:");
            for (int i = 0; i < topStocks.Count; i++)
            {
                Console.WriteLine(
                    $"    {i + 1}. {topStocks[i].Symbol,-12} " +
                    $"Sector: {topStocks[i].SectorName,-25} " +
                    $"1D: {topStocks[i].Pct1d,6:F2}%  " +
                    $"Close: {topStocks[i].PreviousClose:F2}  " +
                    $"Score: {topStocks[i].Score:F3}");
            }

            await _db.InsertTopStocksAsync(runId, businessDate, topStocks);

            Console.WriteLine($"\n   ✅ {topStocks.Count} stocks saved to Table_4_Top_Stocks");

            await _db.InsertRunLogAsync(new RunLog
            {
                RunId = runId,
                BusinessDate = businessDate,
                Stage = "STAGE_5",
                Message = $"Top stocks selected: {topStocks.Count} ({string.Join(", ", selectedSectors)})",
                Status = "SUCCESS"
            });

            Console.WriteLine("✅ STAGE 5 COMPLETE");
            return topStocks;
        }

        // =============================================
        // STAGE 8 — Risk management
        // =============================================
        private async Task<List<TradeSignal>> Stage8_RiskManagement(
            string runId,
            DateTime businessDate,
            List<Stock> stocks)
        {
            if (stocks.Count == 0)
            {
                Console.WriteLine("   ⚠️ No stocks to evaluate");
                return new List<TradeSignal>();
            }

            var signals = _riskManager.ApplyRiskRules(stocks);

            await _db.InsertTradeSignalsAsync(runId, businessDate, signals);

            await _db.InsertRunLogAsync(new RunLog
            {
                RunId = runId,
                BusinessDate = businessDate,
                Stage = "STAGE_8",
                Message = $"BUY:{signals.Count(s => s.Signal == "BUY")} " +
                          $"WATCH:{signals.Count(s => s.Signal == "WATCH")} " +
                          $"SKIP:{signals.Count(s => s.Signal == "SKIP")}",
                Status = "SUCCESS"
            });

            Console.WriteLine("✅ STAGE 8 COMPLETE");
            return signals;
        }

        // =============================================
        // STAGE 9 — Email
        // =============================================
        private async Task Stage9_SendEmail(
            string runId,
            DateTime businessDate,
            List<string> selectedSectors,
            List<TradeSignal> signals)
        {
            await _emailAlert.SendDailySignalAsync(
                runId,
                businessDate,
                selectedSectors,
                signals);

            await _db.InsertRunLogAsync(new RunLog
            {
                RunId = runId,
                BusinessDate = businessDate,
                Stage = "STAGE_9",
                Message = $"Email sent for {signals.Count} signals",
                Status = "SUCCESS"
            });

            Console.WriteLine("✅ STAGE 9 COMPLETE");
        }
    }
}

