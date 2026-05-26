using NEO.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Services
{
    public class TopStockSelectorService
    {
        private readonly ApiDataService _api;
        private readonly StockFilterService _filter;

        public TopStockSelectorService(ApiDataService api)
        {
            _api = api;
            _filter = new StockFilterService();
        }

        // =============================================
        // SELECT TOP STOCKS FOR SELECTED SECTORS
        // =============================================
        public async Task<List<Stock>> SelectTopStocksAsync(
            List<string> selectedSectors,
            string market = "US",
            int topN = 10)
        {
            var allTopStocks = new List<Stock>();

            Console.WriteLine($"\n🔵 STAGE 5 — Selecting Top Stocks...");
            Console.WriteLine($"   → Markets : {market}");
            Console.WriteLine($"   → Sectors : {string.Join(", ", selectedSectors)}");
            Console.WriteLine($"   → Top N   : {topN}");

            foreach (var sector in selectedSectors)
            {
                Console.WriteLine($"\n   📂 Processing sector: {sector}");

                // Fetch stocks from API
                var stocks = await _api.FetchTopStocksForSectorAsync(
                    sector, market, topN: 10);

                if (stocks.Count == 0)
                {
                    Console.WriteLine($"   ⚠️ No stocks returned for {sector}");
                    continue;
                }

                // Filter stocks
                var filtered = _filter.FilterStocks(stocks, sector);

                // Score and rank
                var ranked = _filter.ScoreAndRankStocks(filtered);

                // Take top N
                var top = ranked.Take(topN).ToList();

                Console.WriteLine($"\n   🏆 Top {top.Count} stocks for {sector}:");
                foreach (var s in top)
                    Console.WriteLine($"   {s.Rank,2}. {s.Symbol,-8} " +
                                      $"1D:{s.Pct1d,6:F2}%  " +
                                      $"Score:{s.Score,6:F3}");

                allTopStocks.AddRange(top);
            }

            Console.WriteLine($"\n   ✅ Total top stocks selected: {allTopStocks.Count}");
            return allTopStocks;
        }

        // =============================================
        // SELECT FROM DB DATA — No API calls needed
        // =============================================
        public List<Stock> SelectTopStocksFromMockData(
            List<string> selectedSectors,
            int topN = 10)
        {
            Console.WriteLine($"\n🔵 STAGE 5 — Selecting Top Stocks (Mock Mode)...");

            var allTopStocks = new List<Stock>();

            // Mock stock data per sector
            var mockStocks = GetMockStocks();

            foreach (var sector in selectedSectors)
            {
                Console.WriteLine($"\n   📂 Processing sector: {sector}");

                var stocks = mockStocks
                    .Where(s => s.SectorName.Equals(
                        sector, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (stocks.Count == 0)
                {
                    // Use generic mock stocks if sector not in mock data
                    stocks = GetGenericMockStocks(sector);
                }

                // Filter
                var filtered = _filter.FilterStocks(stocks, sector);

                // Score and rank
                var ranked = _filter.ScoreAndRankStocks(filtered);

                // Take top N
                var top = ranked.Take(topN).ToList();

                Console.WriteLine($"\n   🏆 Top {top.Count} stocks for {sector}:");
                foreach (var s in top)
                    Console.WriteLine($"   {s.Rank,2}. {s.Symbol,-8} " +
                                      $"1D:{s.Pct1d,6:F2}%  " +
                                      $"Score:{s.Score,6:F3}");

                allTopStocks.AddRange(top);
            }

            Console.WriteLine($"\n   ✅ Total top stocks selected: {allTopStocks.Count}");
            return allTopStocks;
        }

        // =============================================
        // MOCK DATA — Used in DB Mode / Weekend Testing
        // =============================================
        private List<Stock> GetMockStocks()
        {
            return new List<Stock>
            {
                // Industrials
                new Stock { Symbol="HON",  StockName="Honeywell",         SectorName="Industrials", Pct1d=2.10m,  PERatio=24.5m, AvgTurnover30d=800_000_000m  },
                new Stock { Symbol="UPS",  StockName="UPS",               SectorName="Industrials", Pct1d=1.85m,  PERatio=19.2m, AvgTurnover30d=600_000_000m  },
                new Stock { Symbol="CAT",  StockName="Caterpillar",       SectorName="Industrials", Pct1d=1.60m,  PERatio=16.8m, AvgTurnover30d=750_000_000m  },
                new Stock { Symbol="GE",   StockName="GE Aerospace",      SectorName="Industrials", Pct1d=1.40m,  PERatio=33.1m, AvgTurnover30d=900_000_000m  },
                new Stock { Symbol="BA",   StockName="Boeing",            SectorName="Industrials", Pct1d=0.90m,  PERatio=0m,    AvgTurnover30d=1_200_000_000m},
                new Stock { Symbol="LMT",  StockName="Lockheed Martin",   SectorName="Industrials", Pct1d=0.75m,  PERatio=17.4m, AvgTurnover30d=400_000_000m  },
                new Stock { Symbol="RTX",  StockName="RTX Corp",          SectorName="Industrials", Pct1d=0.50m,  PERatio=21.3m, AvgTurnover30d=500_000_000m  },
                new Stock { Symbol="DE",   StockName="John Deere",        SectorName="Industrials", Pct1d=-0.20m, PERatio=11.9m, AvgTurnover30d=350_000_000m  },
                new Stock { Symbol="FDX",  StockName="FedEx",             SectorName="Industrials", Pct1d=-0.50m, PERatio=14.2m, AvgTurnover30d=300_000_000m  },
                new Stock { Symbol="EMR",  StockName="Emerson Electric",  SectorName="Industrials", Pct1d=-2.50m, PERatio=20.1m, AvgTurnover30d=250_000_000m  },

                // Health Care
                new Stock { Symbol="JNJ",  StockName="Johnson & Johnson", SectorName="Health Care", Pct1d=1.80m,  PERatio=15.2m, AvgTurnover30d=1_100_000_000m},
                new Stock { Symbol="UNH",  StockName="UnitedHealth",      SectorName="Health Care", Pct1d=1.50m,  PERatio=22.4m, AvgTurnover30d=900_000_000m  },
                new Stock { Symbol="PFE",  StockName="Pfizer",            SectorName="Health Care", Pct1d=1.20m,  PERatio=12.1m, AvgTurnover30d=800_000_000m  },
                new Stock { Symbol="ABBV", StockName="AbbVie",            SectorName="Health Care", Pct1d=0.90m,  PERatio=18.7m, AvgTurnover30d=600_000_000m  },
                new Stock { Symbol="MRK",  StockName="Merck",             SectorName="Health Care", Pct1d=0.60m,  PERatio=14.5m, AvgTurnover30d=700_000_000m  },

                // Consumer Staples
                new Stock { Symbol="PG",   StockName="Procter & Gamble",  SectorName="Consumer Staples", Pct1d=1.10m, PERatio=25.3m, AvgTurnover30d=600_000_000m},
                new Stock { Symbol="KO",   StockName="Coca-Cola",         SectorName="Consumer Staples", Pct1d=0.90m, PERatio=23.1m, AvgTurnover30d=500_000_000m},
                new Stock { Symbol="WMT",  StockName="Walmart",           SectorName="Consumer Staples", Pct1d=0.75m, PERatio=29.4m, AvgTurnover30d=800_000_000m},
                new Stock { Symbol="COST", StockName="Costco",            SectorName="Consumer Staples", Pct1d=0.60m, PERatio=47.2m, AvgTurnover30d=700_000_000m},
                new Stock { Symbol="PEP",  StockName="PepsiCo",           SectorName="Consumer Staples", Pct1d=0.40m, PERatio=24.8m, AvgTurnover30d=450_000_000m},

                // Materials
                new Stock { Symbol="LIN",  StockName="Linde",             SectorName="Materials", Pct1d=1.20m, PERatio=29.1m, AvgTurnover30d=500_000_000m},
                new Stock { Symbol="APD",  StockName="Air Products",      SectorName="Materials", Pct1d=0.95m, PERatio=22.4m, AvgTurnover30d=300_000_000m},
                new Stock { Symbol="NEM",  StockName="Newmont",           SectorName="Materials", Pct1d=0.70m, PERatio=18.6m, AvgTurnover30d=400_000_000m},
                new Stock { Symbol="FCX",  StockName="Freeport-McMoRan",  SectorName="Materials", Pct1d=0.50m, PERatio=16.2m, AvgTurnover30d=600_000_000m},
                new Stock { Symbol="ECL",  StockName="Ecolab",            SectorName="Materials", Pct1d=0.30m, PERatio=35.7m, AvgTurnover30d=200_000_000m},
            };
        }

        private List<Stock> GetGenericMockStocks(string sectorName)
        {
            return new List<Stock>
            {
                new Stock { Symbol="STK1", StockName="Stock 1", SectorName=sectorName, Pct1d=1.50m, PERatio=20m, AvgTurnover30d=500_000_000m },
                new Stock { Symbol="STK2", StockName="Stock 2", SectorName=sectorName, Pct1d=1.20m, PERatio=18m, AvgTurnover30d=400_000_000m },
                new Stock { Symbol="STK3", StockName="Stock 3", SectorName=sectorName, Pct1d=0.90m, PERatio=22m, AvgTurnover30d=300_000_000m },
                new Stock { Symbol="STK4", StockName="Stock 4", SectorName=sectorName, Pct1d=0.60m, PERatio=25m, AvgTurnover30d=200_000_000m },
                new Stock { Symbol="STK5", StockName="Stock 5", SectorName=sectorName, Pct1d=0.30m, PERatio=30m, AvgTurnover30d=100_000_000m },
            };
        }
    }
}
