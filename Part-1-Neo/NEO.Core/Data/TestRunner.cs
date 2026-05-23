using NEO.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Data
{
    public class TestRunner
    {
        private readonly DatabaseHelper _db;

        public TestRunner(string connectionString)
        {
            _db = new DatabaseHelper(connectionString);
        }

        public async Task RunAllTests()
        {
            Console.WriteLine("==============================================");
            Console.WriteLine("PROJECT NEO — Database Connection Tests");
            Console.WriteLine("==============================================");

            // TEST 1 — Connection
            Console.WriteLine("\n🔵 TEST 1 — Testing DB Connection...");
            var connected = await _db.TestConnectionAsync();
            if (connected)
                Console.WriteLine("✅ TEST 1 PASSED — Connected to Azure SQL!");
            else
            {
                Console.WriteLine("❌ TEST 1 FAILED — Cannot connect to DB!");
                Console.WriteLine("   Check your connection string.");
                return;
            }

            // TEST 2 — Forbidden Sectors
            Console.WriteLine("\n🔵 TEST 2 — Reading Forbidden Sectors...");
            var forbidden = await _db.GetForbiddenSectorsAsync();
            if (forbidden.Count > 0)
            {
                Console.WriteLine($"✅ TEST 2 PASSED — Found {forbidden.Count} forbidden sectors:");
                foreach (var s in forbidden)
                    Console.WriteLine($"   → {s}");
            }
            else
                Console.WriteLine("❌ TEST 2 FAILED — No forbidden sectors found!");

            // TEST 3 — Insert Run Log
            Console.WriteLine("\n🔵 TEST 3 — Writing to Run Log...");
            var log = new RunLog
            {
                RunId = "TEST-001",
                BusinessDate = DateTime.Today,
                Stage = "TEST",
                Message = "Database connection test successful",
                Status = "SUCCESS"
            };
            await _db.InsertRunLogAsync(log);
            var count = await _db.GetRowCountAsync("Run_Decision_Log");
            if (count > 0)
                Console.WriteLine($"✅ TEST 3 PASSED — Run log written! Total rows: {count}");
            else
                Console.WriteLine("❌ TEST 3 FAILED — Could not write run log!");

            // TEST 4 — Insert Sector Data
            Console.WriteLine("\n🔵 TEST 4 — Writing Sector Data...");
            var testSectors = new List<Sector>
            {
                new Sector { SectorName = "Technology", PctChange = 2.5m, Rank = 1 },
                new Sector { SectorName = "Pharma",     PctChange = 1.8m, Rank = 2 },
                new Sector { SectorName = "Auto",       PctChange = 1.2m, Rank = 3 }
            };
            await _db.InsertSectorRankingAsync(
                "Table_2_Sector_1D_India",
                "TEST-001",
                DateTime.Today,
                testSectors);
            var sectorCount = await _db.GetRowCountAsync("Table_2_Sector_1D_India");
            if (sectorCount > 0)
                Console.WriteLine($"✅ TEST 4 PASSED — Sectors written! Total rows: {sectorCount}");
            else
                Console.WriteLine("❌ TEST 4 FAILED — Could not write sectors!");

            // TEST 5 — Read Sector Data Back
            Console.WriteLine("\n🔵 TEST 5 — Reading Sector Data Back...");
            var readBack = await _db.GetSectorRankingAsync(
                "Table_2_Sector_1D_India",
                "TEST-001",
                DateTime.Today);
            if (readBack.Count > 0)
            {
                Console.WriteLine($"✅ TEST 5 PASSED — Read {readBack.Count} sectors back:");
                foreach (var s in readBack)
                    Console.WriteLine($"   Rank {s.Rank}: {s.SectorName} → {s.PctChange}%");
            }
            else
                Console.WriteLine("❌ TEST 5 FAILED — Could not read sectors back!");

            // SUMMARY
            Console.WriteLine("\n==============================================");
            Console.WriteLine("✅ ALL TESTS COMPLETE!");
            Console.WriteLine("==============================================");
        }
    }
}
