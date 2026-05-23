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
        private readonly string _apiKey;

        public PipelineOrchestrator(string connectionString, string apiKey)
        {
            _db = new DatabaseHelper(connectionString);
            _api = new ApiDataService(apiKey);
            _apiKey = apiKey;
        }

        // =============================================
        // MASTER RUN — Called by Azure Function
        // =============================================
        public async Task RunDailyPipelineAsync()
        {
            var runId = $"RUN-{DateTime.Now:yyyyMMdd-HHmmss}";
            var businessDate = DateTime.Today;

            Console.WriteLine("==============================================");
            Console.WriteLine($"PROJECT NEO — Daily Pipeline");
            Console.WriteLine($"Run ID    : {runId}");
            Console.WriteLine($"Date      : {businessDate:yyyy-MM-dd}");
            Console.WriteLine("==============================================");

            try
            {
                // STAGE 0 — Test connections
                await Stage0_TestConnections(runId, businessDate);

                // STAGE 1 — Fetch US Sector Data
                await Stage1_FetchUSSectors(runId, businessDate);

                // Log success
                await _db.InsertRunLogAsync(new RunLog
                {
                    RunId = runId,
                    BusinessDate = businessDate,
                    Stage = "PIPELINE",
                    Message = "Daily pipeline completed successfully",
                    Status = "SUCCESS"
                });

                Console.WriteLine("\n==============================================");
                Console.WriteLine("✅ PIPELINE COMPLETE!");
                Console.WriteLine("==============================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ PIPELINE FAILED: {ex.Message}");

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
            Console.WriteLine("\n🔵 STAGE 0 — Testing Connections...");

            // Test DB
            var dbOk = await _db.TestConnectionAsync();
            if (!dbOk)
                throw new Exception("Database connection failed!");
            Console.WriteLine("   ✅ Database connected");

            // Test API
            var apiOk = await _api.TestApiConnectionAsync();
            if (!apiOk)
                throw new Exception("Alpha Vantage API connection failed!");
            Console.WriteLine("   ✅ Alpha Vantage API connected");

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
        // STAGE 1 — Fetch US Sector Rankings
        // =============================================
        private async Task Stage1_FetchUSSectors(string runId, DateTime businessDate)
        {
            Console.WriteLine("\n🔵 STAGE 1 — Fetching US Sector Rankings...");

            // Fetch from API
            var sectors = await _api.FetchUSSectorPerformanceAsync();

            if (sectors.Count == 0)
            {
                Console.WriteLine("   ⚠️ No sectors returned from API");
                await _db.InsertRunLogAsync(new RunLog
                {
                    RunId = runId,
                    BusinessDate = businessDate,
                    Stage = "STAGE_1",
                    Message = "No sectors returned from API",
                    Status = "WARNING"
                });
                return;
            }

            // Get forbidden sectors
            var forbidden = await _db.GetForbiddenSectorsAsync();
            Console.WriteLine($"   → Filtering out {forbidden.Count} forbidden sectors");

            // Filter forbidden
            var filtered = sectors
                .Where(s => !forbidden.Contains(s.SectorName.ToLower()))
                .ToList();

            // Re-rank after filtering
            for (int i = 0; i < filtered.Count; i++)
                filtered[i].Rank = i + 1;

            Console.WriteLine($"   → {sectors.Count} sectors fetched");
            Console.WriteLine($"   → {filtered.Count} sectors after filtering");

            // Save to Table_2_Sector_1D_US
            await _db.InsertSectorRankingAsync(
                "Table_2_Sector_1D_US",
                runId,
                businessDate,
                filtered);

            Console.WriteLine("   ✅ Saved to Table_2_Sector_1D_US");

            // Print rankings
            Console.WriteLine("\n   📊 US Sector Rankings:");
            foreach (var s in filtered.Take(5))
                Console.WriteLine($"   Rank {s.Rank}: {s.SectorName} → {s.PctChange}%");

            await _db.InsertRunLogAsync(new RunLog
            {
                RunId = runId,
                BusinessDate = businessDate,
                Stage = "STAGE_1",
                Message = $"US sectors saved: {filtered.Count} sectors",
                Status = "SUCCESS"
            });

            Console.WriteLine("✅ STAGE 1 COMPLETE");
        }
    }
}
