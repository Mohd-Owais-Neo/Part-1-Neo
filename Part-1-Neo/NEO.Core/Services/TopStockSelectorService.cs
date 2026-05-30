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

        // =============================================
        // STOCK UNIVERSE — Stocks per sector
        // =============================================
        private static readonly Dictionary<string, List<string>> SectorStocks = new()
        {
            ["Industrials"] = new()
            {
                "HON", "UPS", "CAT", "GE", "BA",
                "LMT", "RTX", "DE", "FDX", "EMR"
            },
            ["Technology"] = new()
            {
                "MSFT", "AAPL", "NVDA", "AVGO", "CRM",
                "ORCL", "ACN", "AMD", "INTC", "IBM"
            },
            ["Health Care"] = new()
            {
                "JNJ", "UNH", "LLY", "MRK", "ABT",
                "TMO", "DHR", "BMY", "AMGN", "MDT"
            },
            ["Energy"] = new()
            {
                "XOM", "CVX", "COP", "SLB", "EOG",
                "PXD", "MPC", "VLO", "PSX", "OXY"
            },
            ["Communication Services"] = new()
            {
                "GOOGL", "META", "NFLX", "DIS", "CMCSA",
                "VZ", "T", "TMUS", "ATVI", "EA"
            },
            ["Consumer Discretionary"] = new()
            {
                "AMZN", "TSLA", "MCD", "NKE", "SBUX",
                "HD", "LOW", "TJX", "BKNG", "GM"
            },
            ["Consumer Staples"] = new()
            {
                "PG", "KO", "PEP", "COST", "WMT",
                "PM", "MO", "CL", "GIS", "KHC"
            },
            ["Materials"] = new()
            {
                "LIN", "APD", "SHW", "FCX", "NEM",
                "NUE", "VMC", "MLM", "CE", "ALB"
            },
            ["Real Estate"] = new()
            {
                "AMT", "PLD", "CCI", "EQIX", "PSA",
                "DLR", "O", "WELL", "AVB", "EQR"
            },
            ["Utilities"] = new()
            {
                "NEE", "DUK", "SO", "D", "AEP",
                "EXC", "SRE", "XEL", "ED", "ETR"
            },
            ["Pharma"] = new()
            {
                "JNJ", "MRK", "PFE", "ABBV", "BMY",
                "LLY", "AMGN", "GILD", "BIIB", "REGN"
            },
            ["Auto"] = new()
            {
                "TSLA", "GM", "F", "TM", "HMC",
                "RIVN", "LCID", "NIO", "XPEV", "LI"
            },
            ["Consumer"] = new()
            {
                "AMZN", "WMT", "COST", "TGT", "HD",
                "LOW", "BABA", "JD", "PDD", "MELI"
            },
            ["Metals"] = new()
            {
                "FCX", "NEM", "GOLD", "AEM", "WPM",
                "AA", "NUE", "STLD", "CLF", "MP"
            },
            ["Telecom"] = new()
            {
                "VZ", "T", "TMUS", "LUMN", "USM",
                "SHEN", "TDS", "GSAT", "IRDM", "VSAT"
            },
            ["FMCG"] = new()
            {
                "PG", "KO", "PEP", "UL", "NSRGY",
                "CL", "KMB", "CHD", "SPB", "HRL"
            }
        };

        public TopStockSelectorService(ApiDataService api)
        {
            _api = api;
        }

        // =============================================
        // SELECT TOP STOCKS — REAL API DATA
        // =============================================
        public async Task<List<Stock>> SelectTopStocksAsync(
            List<string> sectors,
            int topN = 10)
        {
            Console.WriteLine("\n🔵 STAGE 5 — Selecting Top Stocks (Real Data)...");

            var allStocks = new List<Stock>();

            foreach (var sector in sectors)
            {
                Console.WriteLine($"\n   📊 Processing sector: {sector}");

                if (!SectorStocks.ContainsKey(sector))
                {
                    Console.WriteLine($"   ⚠️ No stocks defined for {sector}");
                    continue;
                }

                var symbols = SectorStocks[sector];
                var passed = new List<Stock>();
                var failed = 0;

                foreach (var symbol in symbols)
                {
                    // Wait between calls → respect rate limit
                    await Task.Delay(12000); // 12 sec = 5 calls/min

                    var quote = await _api.FetchStockQuoteAsync(symbol);

                    if (quote == null)
                    {
                        Console.WriteLine($"   ⚠️ {symbol,-8} → No data, skipping");
                        failed++;
                        continue;
                    }

                    // Filter: momentum must be > -1%
                    if (quote.Pct1d < -1.0m)
                    {
                        Console.WriteLine($"   ❌ {symbol,-8} " +
                                          $"1D: {quote.Pct1d,6:F2}% → FAILED (momentum too low)");
                        failed++;
                        continue;
                    }

                    Console.WriteLine($"   ✅ {symbol,-8} " +
                                      $"1D: {quote.Pct1d,6:F2}% → PASSED");

                    passed.Add(new Stock
                    {
                        Symbol = symbol,
                        StockName = symbol,
                        SectorName = sector,
                        Pct1d = quote.Pct1d,
                        Price = quote.Price
                    });
                }

                Console.WriteLine($"\n   → {passed.Count} passed / {failed} failed");

                // Score and rank
                var scored = ScoreAndRank(passed, sector);
                allStocks.AddRange(scored.Take(topN));
            }

            Console.WriteLine($"\n   ✅ Total top stocks selected: {allStocks.Count}");
            return allStocks;
        }

        // =============================================
        // MOCK MODE — Used when API limit reached
        // =============================================
        public List<Stock> SelectTopStocksFromMockData(
            List<string> sectors,
            int topN = 10)
        {
            Console.WriteLine("\n🔵 STAGE 5 — Selecting Top Stocks (Mock Mode)...");

            var allStocks = new List<Stock>();

            foreach (var sector in sectors)
            {
                Console.WriteLine($"\n   📊 Processing sector: {sector}");

                var mockStocks = GetMockStocks(sector);
                Console.WriteLine($"   → Filtering {mockStocks.Count} stocks for sector: {sector}");

                var passed = mockStocks
                    .Where(s => {
                        if (s.Pct1d < -1.0m)
                        {
                            Console.WriteLine($"   ❌ {s.Symbol,-8} → FAILED (momentum too low ({s.Pct1d:F2}%))");
                            return false;
                        }
                        Console.WriteLine($"   ✅ {s.Symbol,-8} " +
                                          $"1D: {s.Pct1d,6:F2}% PE: {s.PERatio,5:F1} → PASSED");
                        return true;
                    }).ToList();

                Console.WriteLine($"\n   → {passed.Count} passed / {mockStocks.Count - passed.Count} failed");

                var scored = ScoreAndRank(passed, sector);

                Console.WriteLine("\n   📊 Stock Rankings:");
                foreach (var s in scored.Take(topN))
                    Console.WriteLine($"   Rank {s.Rank}: {s.Symbol,-8} " +
                                      $"1D: {s.Pct1d,6:F2}%  Score: {s.Score:F3}");

                allStocks.AddRange(scored.Take(topN));
            }

            Console.WriteLine($"\n   ✅ Total top stocks selected: {allStocks.Count}");
            return allStocks;
        }

        // =============================================
        // SCORE + RANK STOCKS
        // =============================================
        private List<Stock> ScoreAndRank(List<Stock> stocks, string sector)
        {
            if (stocks.Count == 0) return stocks;

            Console.WriteLine("\n   → Scoring and ranking stocks...");

            foreach (var s in stocks)
            {
                // Score formula:
                // 40% → 1D momentum
                // 30% → PE ratio (lower = better, capped at 30)
                // 30% → Liquidity proxy (price-based)

                var momentumScore = (double)(s.Pct1d + 3m) / 6.0 * 4.0;
                var peScore = s.PERatio > 0
                                    ? Math.Max(0, (30.0 - (double)s.PERatio) / 30.0 * 3.0)
                                    : 1.5;
                var priceScore = s.Price > 0
                                    ? Math.Min((double)s.Price / 500.0 * 3.0, 3.0)
                                    : 1.0;

                s.Score = (decimal)Math.Round(
                    momentumScore * 0.4 + peScore * 0.3 + priceScore * 0.3, 3);
            }

            var ranked = stocks
                .OrderByDescending(s => s.Score)
                .ToList();

            for (int i = 0; i < ranked.Count; i++)
                ranked[i].Rank = i + 1;

            Console.WriteLine("\n   📊 Stock Rankings:");
            foreach (var s in ranked)
                Console.WriteLine($"   Rank {s.Rank}: {s.Symbol,-8} " +
                                  $"1D: {s.Pct1d,6:F2}%  Score: {s.Score:F3}");

            return ranked;
        }

        // =============================================
        // MOCK DATA — Fallback when API limit reached
        // =============================================
        private List<Stock> GetMockStocks(string sector) => sector switch
        {
            "Industrials" => new List<Stock>
    {
        new() { Symbol="HON", StockName="Honeywell",   SectorName=sector, Pct1d= 2.10m, PERatio=24.5m, Price=220m },
        new() { Symbol="UPS", StockName="UPS",         SectorName=sector, Pct1d= 1.85m, PERatio=19.2m, Price=145m },
        new() { Symbol="CAT", StockName="Caterpillar", SectorName=sector, Pct1d= 1.60m, PERatio=16.8m, Price=340m },
        new() { Symbol="GE",  StockName="GE",          SectorName=sector, Pct1d= 1.40m, PERatio=33.1m, Price=175m },
        new() { Symbol="BA",  StockName="Boeing",      SectorName=sector, Pct1d= 0.90m, PERatio= 0.0m, Price=210m },
        new() { Symbol="LMT", StockName="Lockheed",    SectorName=sector, Pct1d= 0.75m, PERatio=17.4m, Price=480m },
        new() { Symbol="RTX", StockName="Raytheon",    SectorName=sector, Pct1d= 0.50m, PERatio=21.3m, Price=112m },
        new() { Symbol="DE",  StockName="Deere",       SectorName=sector, Pct1d=-0.20m, PERatio=11.9m, Price=415m },
        new() { Symbol="FDX", StockName="FedEx",       SectorName=sector, Pct1d=-0.50m, PERatio=14.2m, Price=265m },
        new() { Symbol="EMR", StockName="Emerson",     SectorName=sector, Pct1d=-2.50m, PERatio=22.1m, Price=115m },
    },
            "Technology" => new List<Stock>
    {
        new() { Symbol="MSFT", StockName="Microsoft",  SectorName=sector, Pct1d= 5.45m, PERatio=35.2m, Price=450m },
        new() { Symbol="AAPL", StockName="Apple",      SectorName=sector, Pct1d= 3.20m, PERatio=29.8m, Price=210m },
        new() { Symbol="NVDA", StockName="Nvidia",     SectorName=sector, Pct1d= 4.80m, PERatio=42.1m, Price=135m },
        new() { Symbol="AVGO", StockName="Broadcom",   SectorName=sector, Pct1d= 2.10m, PERatio=26.4m, Price=220m },
        new() { Symbol="CRM",  StockName="Salesforce", SectorName=sector, Pct1d= 1.75m, PERatio=31.5m, Price=285m },
        new() { Symbol="ORCL", StockName="Oracle",     SectorName=sector, Pct1d= 1.50m, PERatio=22.8m, Price=175m },
        new() { Symbol="ACN",  StockName="Accenture",  SectorName=sector, Pct1d= 1.20m, PERatio=28.3m, Price=315m },
        new() { Symbol="AMD",  StockName="AMD",        SectorName=sector, Pct1d= 0.90m, PERatio=44.2m, Price=165m },
        new() { Symbol="INTC", StockName="Intel",      SectorName=sector, Pct1d=-0.30m, PERatio=18.5m, Price= 22m },
        new() { Symbol="IBM",  StockName="IBM",        SectorName=sector, Pct1d=-0.80m, PERatio=20.1m, Price=235m },
    },
            "Real Estate" => new List<Stock>
    {
        new() { Symbol="AMT",  StockName="American Tower", SectorName=sector, Pct1d= 1.50m, PERatio=42.3m, Price=185m },
        new() { Symbol="PLD",  StockName="Prologis",       SectorName=sector, Pct1d= 1.20m, PERatio=35.8m, Price=115m },
        new() { Symbol="CCI",  StockName="Crown Castle",   SectorName=sector, Pct1d= 0.80m, PERatio=38.5m, Price= 95m },
        new() { Symbol="EQIX", StockName="Equinix",        SectorName=sector, Pct1d= 0.60m, PERatio=44.1m, Price=820m },
        new() { Symbol="PSA",  StockName="Public Storage",  SectorName=sector, Pct1d= 0.40m, PERatio=29.6m, Price=295m },
        new() { Symbol="DLR",  StockName="Digital Realty",  SectorName=sector, Pct1d= 0.20m, PERatio=31.2m, Price=145m },
        new() { Symbol="O",    StockName="Realty Income",   SectorName=sector, Pct1d=-0.10m, PERatio=44.8m, Price= 55m },
        new() { Symbol="WELL", StockName="Welltower",       SectorName=sector, Pct1d=-0.40m, PERatio=88.5m, Price=135m },
        new() { Symbol="AVB",  StockName="AvalonBay",       SectorName=sector, Pct1d=-0.60m, PERatio=32.4m, Price=210m },
        new() { Symbol="EQR",  StockName="Equity Residential",SectorName=sector,Pct1d=-0.90m, PERatio=28.7m, Price= 72m },
    },
            _ => new List<Stock>()
        };
    }
}


