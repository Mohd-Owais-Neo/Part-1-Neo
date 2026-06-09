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
                        Price = quote.Price,
                        PreviousClose = quote.Price  // ← FIXED: map Price → PreviousClose
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
                // Use PreviousClose if Price is missing
                var effectivePrice = s.Price > 0 ? s.Price : s.PreviousClose;
                var priceScore = effectivePrice > 0
                                        ? Math.Min((double)effectivePrice / 500.0 * 3.0, 3.0)
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
        // =============================================
        // MOCK DATA — Fallback when API limit reached
        // Full coverage for ALL sectors
        // =============================================
        // =============================================
        // MOCK DATA — Indian (NSE) stocks per sector
        // =============================================
        private List<Stock> GetMockStocks(string sector) => sector switch
        {
            "Industrials" => new List<Stock>
    {
        new() { Symbol="LT",         StockName="L&T",                SectorName=sector, Pct1d= 2.10m, Pct5d= 3.90m, PERatio=28.4m, Price=3580m,  PreviousClose=3580m  },
        new() { Symbol="SIEMENS",    StockName="Siemens India",      SectorName=sector, Pct1d= 1.85m, Pct5d= 3.50m, PERatio=44.6m, Price=6840m,  PreviousClose=6840m  },
        new() { Symbol="ABB",        StockName="ABB India",          SectorName=sector, Pct1d= 1.60m, Pct5d= 3.00m, PERatio=42.8m, Price=8120m,  PreviousClose=8120m  },
        new() { Symbol="BHEL",       StockName="BHEL",               SectorName=sector, Pct1d= 1.40m, Pct5d= 2.60m, PERatio=32.4m, Price=285m,   PreviousClose=285m   },
        new() { Symbol="HAL",        StockName="HAL",                SectorName=sector, Pct1d= 1.20m, Pct5d= 2.20m, PERatio=36.2m, Price=4650m,  PreviousClose=4650m  },
        new() { Symbol="CUMMINSIND", StockName="Cummins India",      SectorName=sector, Pct1d= 0.90m, Pct5d= 1.70m, PERatio=38.4m, Price=3280m,  PreviousClose=3280m  },
        new() { Symbol="THERMAX",    StockName="Thermax",            SectorName=sector, Pct1d= 0.70m, Pct5d= 1.30m, PERatio=44.2m, Price=4180m,  PreviousClose=4180m  },
        new() { Symbol="IRCON",      StockName="IRCON Intl",         SectorName=sector, Pct1d= 0.50m, Pct5d= 0.90m, PERatio=22.6m, Price=245m,   PreviousClose=245m   },
        new() { Symbol="RITES",      StockName="RITES",              SectorName=sector, Pct1d=-0.30m, Pct5d=-0.60m, PERatio=18.4m, Price=685m,   PreviousClose=685m   },
        new() { Symbol="BEL",        StockName="BEL",                SectorName=sector, Pct1d=-0.70m, Pct5d=-1.30m, PERatio=38.2m, Price=285m,   PreviousClose=285m   },
    },

            "Technology" => new List<Stock>
    {
        new() { Symbol="TCS",        StockName="TCS",                SectorName=sector, Pct1d= 2.80m, Pct5d= 5.20m, PERatio=28.4m, Price=3985m,  PreviousClose=3985m  },
        new() { Symbol="INFY",       StockName="Infosys",            SectorName=sector, Pct1d= 2.20m, Pct5d= 4.10m, PERatio=24.6m, Price=1685m,  PreviousClose=1685m  },
        new() { Symbol="WIPRO",      StockName="Wipro",              SectorName=sector, Pct1d= 1.80m, Pct5d= 3.40m, PERatio=22.8m, Price=485m,   PreviousClose=485m   },
        new() { Symbol="HCLTECH",    StockName="HCL Tech",           SectorName=sector, Pct1d= 1.50m, Pct5d= 2.80m, PERatio=26.4m, Price=1580m,  PreviousClose=1580m  },
        new() { Symbol="TECHM",      StockName="Tech Mahindra",      SectorName=sector, Pct1d= 1.20m, Pct5d= 2.20m, PERatio=20.4m, Price=1485m,  PreviousClose=1485m  },
        new() { Symbol="LTTS",       StockName="LTIMindtree",        SectorName=sector, Pct1d= 0.90m, Pct5d= 1.70m, PERatio=32.6m, Price=5280m,  PreviousClose=5280m  },
        new() { Symbol="MPHASIS",    StockName="Mphasis",            SectorName=sector, Pct1d= 0.60m, Pct5d= 1.10m, PERatio=28.8m, Price=2680m,  PreviousClose=2680m  },
        new() { Symbol="PERSISTENT", StockName="Persistent Systems", SectorName=sector, Pct1d= 0.40m, Pct5d= 0.80m, PERatio=44.2m, Price=5840m,  PreviousClose=5840m  },
        new() { Symbol="COFORGE",    StockName="Coforge",            SectorName=sector, Pct1d=-0.40m, Pct5d=-0.80m, PERatio=38.4m, Price=7280m,  PreviousClose=7280m  },
        new() { Symbol="KPITTECH",   StockName="KPIT Technologies",  SectorName=sector, Pct1d=-0.90m, Pct5d=-1.70m, PERatio=42.6m, Price=1580m,  PreviousClose=1580m  },
    },

            "Health Care" => new List<Stock>
    {
        new() { Symbol="SUNPHARMA",  StockName="Sun Pharma",         SectorName=sector, Pct1d= 2.80m, Pct5d= 5.20m, PERatio=28.4m, Price=1680m,  PreviousClose=1680m  },
        new() { Symbol="DRREDDY",    StockName="Dr Reddy's",         SectorName=sector, Pct1d= 2.20m, Pct5d= 4.10m, PERatio=22.6m, Price=1285m,  PreviousClose=1285m  },
        new() { Symbol="CIPLA",      StockName="Cipla",              SectorName=sector, Pct1d= 1.90m, Pct5d= 3.60m, PERatio=24.8m, Price=1520m,  PreviousClose=1520m  },
        new() { Symbol="DIVISLAB",   StockName="Divi's Labs",        SectorName=sector, Pct1d= 1.60m, Pct5d= 3.10m, PERatio=35.2m, Price=3840m,  PreviousClose=3840m  },
        new() { Symbol="APOLLOHOSP", StockName="Apollo Hospitals",   SectorName=sector, Pct1d= 1.30m, Pct5d= 2.50m, PERatio=42.1m, Price=6420m,  PreviousClose=6420m  },
        new() { Symbol="TORNTPHARM", StockName="Torrent Pharma",     SectorName=sector, Pct1d= 1.00m, Pct5d= 1.90m, PERatio=38.6m, Price=3180m,  PreviousClose=3180m  },
        new() { Symbol="LUPIN",      StockName="Lupin",              SectorName=sector, Pct1d= 0.80m, Pct5d= 1.50m, PERatio=26.4m, Price=2140m,  PreviousClose=2140m  },
        new() { Symbol="AUROPHARMA", StockName="Aurobindo Pharma",   SectorName=sector, Pct1d= 0.50m, Pct5d= 0.90m, PERatio=18.2m, Price=1180m,  PreviousClose=1180m  },
        new() { Symbol="BIOCON",     StockName="Biocon",             SectorName=sector, Pct1d=-0.30m, Pct5d=-0.60m, PERatio=32.8m, Price=320m,   PreviousClose=320m   },
        new() { Symbol="GLENMARK",   StockName="Glenmark",           SectorName=sector, Pct1d=-0.80m, Pct5d=-1.50m, PERatio=20.4m, Price=985m,   PreviousClose=985m   },
    },

            "Consumer Discretionary" => new List<Stock>
    {
        new() { Symbol="TITAN",      StockName="Titan Company",      SectorName=sector, Pct1d= 3.20m, Pct5d= 5.80m, PERatio=44.2m, Price=3520m,  PreviousClose=3520m  },
        new() { Symbol="MARUTI",     StockName="Maruti Suzuki",      SectorName=sector, Pct1d= 2.50m, Pct5d= 4.60m, PERatio=24.8m, Price=12450m, PreviousClose=12450m },
        new() { Symbol="TATAMOTORS", StockName="Tata Motors",        SectorName=sector, Pct1d= 2.10m, Pct5d= 3.90m, PERatio=10.4m, Price=965m,   PreviousClose=965m   },
        new() { Symbol="BAJAJ-AUTO", StockName="Bajaj Auto",         SectorName=sector, Pct1d= 1.80m, Pct5d= 3.30m, PERatio=22.6m, Price=8840m,  PreviousClose=8840m  },
        new() { Symbol="HEROMOTOCO", StockName="Hero MotoCorp",      SectorName=sector, Pct1d= 1.50m, Pct5d= 2.80m, PERatio=18.4m, Price=4320m,  PreviousClose=4320m  },
        new() { Symbol="MRF",        StockName="MRF",                SectorName=sector, Pct1d= 1.20m, Pct5d= 2.20m, PERatio=28.6m, Price=128500m,PreviousClose=128500m},
        new() { Symbol="BATA",       StockName="Bata India",         SectorName=sector, Pct1d= 0.90m, Pct5d= 1.70m, PERatio=32.4m, Price=1420m,  PreviousClose=1420m  },
        new() { Symbol="PAGEIND",    StockName="Page Industries",    SectorName=sector, Pct1d= 0.60m, Pct5d= 1.10m, PERatio=44.8m, Price=38500m, PreviousClose=38500m },
        new() { Symbol="WHIRLPOOL",  StockName="Whirlpool India",    SectorName=sector, Pct1d=-0.40m, Pct5d=-0.80m, PERatio=36.2m, Price=1580m,  PreviousClose=1580m  },
        new() { Symbol="VOLTAS",     StockName="Voltas",             SectorName=sector, Pct1d=-0.90m, Pct5d=-1.70m, PERatio=38.4m, Price=1285m,  PreviousClose=1285m  },
    },

            "Energy" => new List<Stock>
    {
        new() { Symbol="RELIANCE",   StockName="Reliance Industries",SectorName=sector, Pct1d= 2.50m, Pct5d= 4.80m, PERatio=24.6m, Price=2985m,  PreviousClose=2985m  },
        new() { Symbol="ONGC",       StockName="ONGC",               SectorName=sector, Pct1d= 2.10m, Pct5d= 3.90m, PERatio=8.4m,  Price=265m,   PreviousClose=265m   },
        new() { Symbol="IOC",        StockName="Indian Oil",         SectorName=sector, Pct1d= 1.80m, Pct5d= 3.30m, PERatio=6.8m,  Price=168m,   PreviousClose=168m   },
        new() { Symbol="BPCL",       StockName="BPCL",               SectorName=sector, Pct1d= 1.50m, Pct5d= 2.80m, PERatio=7.2m,  Price=325m,   PreviousClose=325m   },
        new() { Symbol="NTPC",       StockName="NTPC",               SectorName=sector, Pct1d= 1.20m, Pct5d= 2.20m, PERatio=14.8m, Price=368m,   PreviousClose=368m   },
        new() { Symbol="POWERGRID",  StockName="Power Grid",         SectorName=sector, Pct1d= 0.90m, Pct5d= 1.70m, PERatio=16.4m, Price=325m,   PreviousClose=325m   },
        new() { Symbol="ADANIGREEN", StockName="Adani Green Energy", SectorName=sector, Pct1d= 0.60m, Pct5d= 1.10m, PERatio=0.0m,  Price=1685m,  PreviousClose=1685m  },
        new() { Symbol="TATAPOWER",  StockName="Tata Power",         SectorName=sector, Pct1d= 0.40m, Pct5d= 0.80m, PERatio=28.6m, Price=425m,   PreviousClose=425m   },
        new() { Symbol="GAIL",       StockName="GAIL",               SectorName=sector, Pct1d=-0.40m, Pct5d=-0.80m, PERatio=12.4m, Price=215m,   PreviousClose=215m   },
        new() { Symbol="HINDPETRO",  StockName="HPCL",               SectorName=sector, Pct1d=-0.90m, Pct5d=-1.70m, PERatio=9.8m,  Price=385m,   PreviousClose=385m   },
    },

            "Materials" => new List<Stock>
    {
        new() { Symbol="JSWSTEEL",   StockName="JSW Steel",          SectorName=sector, Pct1d= 2.20m, Pct5d= 4.10m, PERatio=14.8m, Price=885m,   PreviousClose=885m   },
        new() { Symbol="TATASTEEL",  StockName="Tata Steel",         SectorName=sector, Pct1d= 1.80m, Pct5d= 3.30m, PERatio=10.4m, Price=165m,   PreviousClose=165m   },
        new() { Symbol="HINDALCO",   StockName="Hindalco",           SectorName=sector, Pct1d= 1.50m, Pct5d= 2.80m, PERatio=12.6m, Price=685m,   PreviousClose=685m   },
        new() { Symbol="SAIL",       StockName="SAIL",               SectorName=sector, Pct1d= 1.20m, Pct5d= 2.20m, PERatio=8.4m,  Price=125m,   PreviousClose=125m   },
        new() { Symbol="VEDL",       StockName="Vedanta",            SectorName=sector, Pct1d= 0.90m, Pct5d= 1.70m, PERatio=10.8m, Price=465m,   PreviousClose=465m   },
        new() { Symbol="COALINDIA",  StockName="Coal India",         SectorName=sector, Pct1d= 0.60m, Pct5d= 1.10m, PERatio=7.4m,  Price=465m,   PreviousClose=465m   },
        new() { Symbol="NMDC",       StockName="NMDC",               SectorName=sector, Pct1d= 0.40m, Pct5d= 0.80m, PERatio=9.2m,  Price=68m,    PreviousClose=68m    },
        new() { Symbol="NATIONALUM", StockName="National Aluminium", SectorName=sector, Pct1d= 0.20m, Pct5d= 0.40m, PERatio=11.4m, Price=185m,   PreviousClose=185m   },
        new() { Symbol="GRASIM",     StockName="Grasim Industries",  SectorName=sector, Pct1d=-0.50m, Pct5d=-1.00m, PERatio=18.6m, Price=2680m,  PreviousClose=2680m  },
        new() { Symbol="ULTRACEMCO", StockName="UltraTech Cement",   SectorName=sector, Pct1d=-0.90m, Pct5d=-1.70m, PERatio=28.4m, Price=10850m, PreviousClose=10850m },
    },

            "Consumer Staples" => new List<Stock>
    {
        new() { Symbol="HINDUNILVR", StockName="HUL",                SectorName=sector, Pct1d= 1.80m, Pct5d= 3.40m, PERatio=58.4m, Price=2485m,  PreviousClose=2485m  },
        new() { Symbol="NESTLEIND",  StockName="Nestlé India",       SectorName=sector, Pct1d= 1.50m, Pct5d= 2.80m, PERatio=64.2m, Price=2285m,  PreviousClose=2285m  },
        new() { Symbol="DABUR",      StockName="Dabur India",        SectorName=sector, Pct1d= 1.20m, Pct5d= 2.20m, PERatio=42.6m, Price=585m,   PreviousClose=585m   },
        new() { Symbol="MARICO",     StockName="Marico",             SectorName=sector, Pct1d= 0.90m, Pct5d= 1.70m, PERatio=44.8m, Price=685m,   PreviousClose=685m   },
        new() { Symbol="GODREJCP",   StockName="Godrej Consumer",    SectorName=sector, Pct1d= 0.70m, Pct5d= 1.30m, PERatio=38.4m, Price=1285m,  PreviousClose=1285m  },
        new() { Symbol="BRITANNIA",  StockName="Britannia",          SectorName=sector, Pct1d= 0.50m, Pct5d= 0.90m, PERatio=48.6m, Price=5480m,  PreviousClose=5480m  },
        new() { Symbol="ITC",        StockName="ITC",                SectorName=sector, Pct1d= 0.30m, Pct5d= 0.60m, PERatio=22.8m, Price=465m,   PreviousClose=465m   },
        new() { Symbol="EMAMILTD",   StockName="Emami",              SectorName=sector, Pct1d= 0.10m, Pct5d= 0.20m, PERatio=32.4m, Price=685m,   PreviousClose=685m   },
        new() { Symbol="TATACONSUM", StockName="Tata Consumer",      SectorName=sector, Pct1d=-0.40m, Pct5d=-0.80m, PERatio=62.4m, Price=985m,   PreviousClose=985m   },
        new() { Symbol="VBL",        StockName="Varun Beverages",    SectorName=sector, Pct1d=-0.80m, Pct5d=-1.50m, PERatio=44.6m, Price=1685m,  PreviousClose=1685m  },
    },

            "Communication Services" => new List<Stock>
    {
        new() { Symbol="BHARTIARTL", StockName="Bharti Airtel",      SectorName=sector, Pct1d= 2.40m, Pct5d= 4.50m, PERatio=44.8m, Price=1685m,  PreviousClose=1685m  },
        new() { Symbol="ZOMATO",     StockName="Zomato",             SectorName=sector, Pct1d= 1.80m, Pct5d= 3.30m, PERatio=0.0m,  Price=245m,   PreviousClose=245m   },
        new() { Symbol="INDIAMART",  StockName="IndiaMart",          SectorName=sector, Pct1d= 1.40m, Pct5d= 2.60m, PERatio=42.6m, Price=2485m,  PreviousClose=2485m  },
        new() { Symbol="NAUKRI",     StockName="Info Edge",          SectorName=sector, Pct1d= 1.10m, Pct5d= 2.00m, PERatio=68.4m, Price=7285m,  PreviousClose=7285m  },
        new() { Symbol="POLICYBZR",  StockName="PB Fintech",         SectorName=sector, Pct1d= 0.80m, Pct5d= 1.50m, PERatio=0.0m,  Price=1685m,  PreviousClose=1685m  },
        new() { Symbol="DELHIVERY",  StockName="Delhivery",          SectorName=sector, Pct1d= 0.60m, Pct5d= 1.10m, PERatio=0.0m,  Price=385m,   PreviousClose=385m   },
        new() { Symbol="IRCTC",      StockName="IRCTC",              SectorName=sector, Pct1d= 0.40m, Pct5d= 0.80m, PERatio=44.2m, Price=785m,   PreviousClose=785m   },
        new() { Symbol="IDEA",       StockName="Vodafone Idea",      SectorName=sector, Pct1d= 0.20m, Pct5d= 0.40m, PERatio=0.0m,  Price=12m,    PreviousClose=12m    },
        new() { Symbol="JUSTDIAL",   StockName="Just Dial",          SectorName=sector, Pct1d=-0.30m, Pct5d=-0.60m, PERatio=22.8m, Price=985m,   PreviousClose=985m   },
        new() { Symbol="SWIGGY",     StockName="Swiggy",             SectorName=sector, Pct1d=-0.80m, Pct5d=-1.50m, PERatio=0.0m,  Price=385m,   PreviousClose=385m   },
    },

            "Real Estate" => new List<Stock>
    {
        new() { Symbol="DLF",        StockName="DLF",                SectorName=sector, Pct1d= 2.10m, Pct5d= 3.90m, PERatio=44.8m, Price=885m,   PreviousClose=885m   },
        new() { Symbol="GODREJPROP", StockName="Godrej Properties",  SectorName=sector, Pct1d= 1.80m, Pct5d= 3.30m, PERatio=0.0m,  Price=2685m,  PreviousClose=2685m  },
        new() { Symbol="OBEROIRLTY", StockName="Oberoi Realty",      SectorName=sector, Pct1d= 1.50m, Pct5d= 2.80m, PERatio=28.6m, Price=1885m,  PreviousClose=1885m  },
        new() { Symbol="PRESTIGE",   StockName="Prestige Estates",   SectorName=sector, Pct1d= 1.20m, Pct5d= 2.20m, PERatio=38.4m, Price=1685m,  PreviousClose=1685m  },
        new() { Symbol="PHOENIXLTD", StockName="Phoenix Mills",      SectorName=sector, Pct1d= 0.90m, Pct5d= 1.70m, PERatio=32.6m, Price=1685m,  PreviousClose=1685m  },
        new() { Symbol="BRIGADE",    StockName="Brigade Enterprises", SectorName=sector, Pct1d= 0.70m, Pct5d= 1.30m, PERatio=28.8m, Price=985m,   PreviousClose=985m   },
        new() { Symbol="SOBHA",      StockName="Sobha",              SectorName=sector, Pct1d= 0.50m, Pct5d= 0.90m, PERatio=44.2m, Price=1285m,  PreviousClose=1285m  },
        new() { Symbol="MAHINDCIE",  StockName="Mahindra Lifespace", SectorName=sector, Pct1d= 0.30m, Pct5d= 0.60m, PERatio=0.0m,  Price=485m,   PreviousClose=485m   },
        new() { Symbol="NXTDIGITAL", StockName="NXT Digital",        SectorName=sector, Pct1d=-0.40m, Pct5d=-0.80m, PERatio=18.4m, Price=285m,   PreviousClose=285m   },
        new() { Symbol="SUNTECK",    StockName="Sunteck Realty",     SectorName=sector, Pct1d=-0.80m, Pct5d=-1.50m, PERatio=22.6m, Price=585m,   PreviousClose=585m   },
    },

            "Utilities" => new List<Stock>
    {
        new() { Symbol="NTPC",       StockName="NTPC",               SectorName=sector, Pct1d= 1.80m, Pct5d= 3.30m, PERatio=14.8m, Price=368m,   PreviousClose=368m   },
        new() { Symbol="POWERGRID",  StockName="Power Grid",         SectorName=sector, Pct1d= 1.40m, Pct5d= 2.60m, PERatio=16.4m, Price=325m,   PreviousClose=325m   },
        new() { Symbol="TATAPOWER",  StockName="Tata Power",         SectorName=sector, Pct1d= 1.10m, Pct5d= 2.00m, PERatio=28.6m, Price=425m,   PreviousClose=425m   },
        new() { Symbol="ADANIGREEN", StockName="Adani Green Energy", SectorName=sector, Pct1d= 0.80m, Pct5d= 1.50m, PERatio=0.0m,  Price=1685m,  PreviousClose=1685m  },
        new() { Symbol="ADANIPOWER", StockName="Adani Power",        SectorName=sector, Pct1d= 0.60m, Pct5d= 1.10m, PERatio=0.0m,  Price=585m,   PreviousClose=585m   },
        new() { Symbol="TORNTPOWER", StockName="Torrent Power",      SectorName=sector, Pct1d= 0.40m, Pct5d= 0.80m, PERatio=18.4m, Price=1285m,  PreviousClose=1285m  },
        new() { Symbol="CESC",       StockName="CESC",               SectorName=sector, Pct1d= 0.20m, Pct5d= 0.40m, PERatio=8.6m,  Price=185m,   PreviousClose=185m   },
        new() { Symbol="JSPL",       StockName="JSPL",               SectorName=sector, Pct1d= 0.10m, Pct5d= 0.20m, PERatio=12.4m, Price=985m,   PreviousClose=985m   },
        new() { Symbol="NHPC",       StockName="NHPC",               SectorName=sector, Pct1d=-0.30m, Pct5d=-0.60m, PERatio=16.8m, Price=85m,    PreviousClose=85m    },
        new() { Symbol="SJVN",       StockName="SJVN",               SectorName=sector, Pct1d=-0.70m, Pct5d=-1.30m, PERatio=22.4m, Price=108m,   PreviousClose=108m   },
    },

            "Pharma" => new List<Stock>
    {
        new() { Symbol="SUNPHARMA",  StockName="Sun Pharma",         SectorName=sector, Pct1d= 2.80m, Pct5d= 5.20m, PERatio=28.4m, Price=1680m,  PreviousClose=1680m  },
        new() { Symbol="DRREDDY",    StockName="Dr Reddy's",         SectorName=sector, Pct1d= 2.20m, Pct5d= 4.10m, PERatio=22.6m, Price=1285m,  PreviousClose=1285m  },
        new() { Symbol="CIPLA",      StockName="Cipla",              SectorName=sector, Pct1d= 1.80m, Pct5d= 3.40m, PERatio=24.8m, Price=1520m,  PreviousClose=1520m  },
        new() { Symbol="DIVISLAB",   StockName="Divi's Labs",        SectorName=sector, Pct1d= 1.50m, Pct5d= 2.80m, PERatio=35.2m, Price=3840m,  PreviousClose=3840m  },
        new() { Symbol="LUPIN",      StockName="Lupin",              SectorName=sector, Pct1d= 1.20m, Pct5d= 2.20m, PERatio=26.4m, Price=2140m,  PreviousClose=2140m  },
        new() { Symbol="TORNTPHARM", StockName="Torrent Pharma",     SectorName=sector, Pct1d= 0.90m, Pct5d= 1.70m, PERatio=38.6m, Price=3180m,  PreviousClose=3180m  },
        new() { Symbol="AUROPHARMA", StockName="Aurobindo Pharma",   SectorName=sector, Pct1d= 0.60m, Pct5d= 1.10m, PERatio=18.2m, Price=1180m,  PreviousClose=1180m  },
        new() { Symbol="ALKEM",      StockName="Alkem Laboratories", SectorName=sector, Pct1d= 0.30m, Pct5d= 0.60m, PERatio=22.8m, Price=5280m,  PreviousClose=5280m  },
        new() { Symbol="BIOCON",     StockName="Biocon",             SectorName=sector, Pct1d=-0.40m, Pct5d=-0.80m, PERatio=32.8m, Price=320m,   PreviousClose=320m   },
        new() { Symbol="GLENMARK",   StockName="Glenmark",           SectorName=sector, Pct1d=-0.80m, Pct5d=-1.50m, PERatio=20.4m, Price=985m,   PreviousClose=985m   },
    },

            "Auto" => new List<Stock>
    {
        new() { Symbol="MARUTI",     StockName="Maruti Suzuki",      SectorName=sector, Pct1d= 2.50m, Pct5d= 4.60m, PERatio=24.8m, Price=12450m, PreviousClose=12450m },
        new() { Symbol="TATAMOTORS", StockName="Tata Motors",        SectorName=sector, Pct1d= 2.10m, Pct5d= 3.90m, PERatio=10.4m, Price=965m,   PreviousClose=965m   },
        new() { Symbol="BAJAJ-AUTO", StockName="Bajaj Auto",         SectorName=sector, Pct1d= 1.80m, Pct5d= 3.30m, PERatio=22.6m, Price=8840m,  PreviousClose=8840m  },
        new() { Symbol="HEROMOTOCO", StockName="Hero MotoCorp",      SectorName=sector, Pct1d= 1.50m, Pct5d= 2.80m, PERatio=18.4m, Price=4320m,  PreviousClose=4320m  },
        new() { Symbol="EICHERMOT",  StockName="Eicher Motors",      SectorName=sector, Pct1d= 1.20m, Pct5d= 2.20m, PERatio=28.6m, Price=4680m,  PreviousClose=4680m  },
        new() { Symbol="M&M",        StockName="Mahindra & Mahindra",SectorName=sector, Pct1d= 0.90m, Pct5d= 1.70m, PERatio=22.4m, Price=2985m,  PreviousClose=2985m  },
        new() { Symbol="ASHOKLEY",   StockName="Ashok Leyland",      SectorName=sector, Pct1d= 0.60m, Pct5d= 1.10m, PERatio=18.6m, Price=245m,   PreviousClose=245m   },
        new() { Symbol="BHARATFORG", StockName="Bharat Forge",       SectorName=sector, Pct1d= 0.40m, Pct5d= 0.80m, PERatio=32.4m, Price=1485m,  PreviousClose=1485m  },
        new() { Symbol="BOSCHLTD",   StockName="Bosch India",        SectorName=sector, Pct1d=-0.30m, Pct5d=-0.60m, PERatio=44.2m, Price=38500m, PreviousClose=38500m },
        new() { Symbol="MOTHERSON",  StockName="Motherson Sumi",     SectorName=sector, Pct1d=-0.80m, Pct5d=-1.50m, PERatio=24.6m, Price=185m,   PreviousClose=185m   },
    },

            "Consumer" => new List<Stock>
    {
        new() { Symbol="TITAN",      StockName="Titan Company",      SectorName=sector, Pct1d= 3.20m, Pct5d= 5.80m, PERatio=44.2m, Price=3520m,  PreviousClose=3520m  },
        new() { Symbol="HINDUNILVR", StockName="HUL",                SectorName=sector, Pct1d= 1.80m, Pct5d= 3.40m, PERatio=58.4m, Price=2485m,  PreviousClose=2485m  },
        new() { Symbol="ITC",        StockName="ITC",                SectorName=sector, Pct1d= 1.50m, Pct5d= 2.80m, PERatio=22.8m, Price=465m,   PreviousClose=465m   },
        new() { Symbol="TATACONSUM", StockName="Tata Consumer",      SectorName=sector, Pct1d= 1.20m, Pct5d= 2.20m, PERatio=62.4m, Price=985m,   PreviousClose=985m   },
        new() { Symbol="DABUR",      StockName="Dabur India",        SectorName=sector, Pct1d= 0.90m, Pct5d= 1.70m, PERatio=42.6m, Price=585m,   PreviousClose=585m   },
        new() { Symbol="MARICO",     StockName="Marico",             SectorName=sector, Pct1d= 0.70m, Pct5d= 1.30m, PERatio=44.8m, Price=685m,   PreviousClose=685m   },
        new() { Symbol="BRITANNIA",  StockName="Britannia",          SectorName=sector, Pct1d= 0.50m, Pct5d= 0.90m, PERatio=48.6m, Price=5480m,  PreviousClose=5480m  },
        new() { Symbol="GODREJCP",   StockName="Godrej Consumer",    SectorName=sector, Pct1d= 0.30m, Pct5d= 0.60m, PERatio=38.4m, Price=1285m,  PreviousClose=1285m  },
        new() { Symbol="VBL",        StockName="Varun Beverages",    SectorName=sector, Pct1d=-0.40m, Pct5d=-0.80m, PERatio=44.6m, Price=1685m,  PreviousClose=1685m  },
        new() { Symbol="NESTLEIND",  StockName="Nestlé India",       SectorName=sector, Pct1d=-0.80m, Pct5d=-1.50m, PERatio=64.2m, Price=2285m,  PreviousClose=2285m  },
    },

            "Metals" => new List<Stock>
    {
        new() { Symbol="JSWSTEEL",   StockName="JSW Steel",          SectorName=sector, Pct1d= 2.40m, Pct5d= 4.50m, PERatio=14.8m, Price=885m,   PreviousClose=885m   },
        new() { Symbol="TATASTEEL",  StockName="Tata Steel",         SectorName=sector, Pct1d= 2.00m, Pct5d= 3.70m, PERatio=10.4m, Price=165m,   PreviousClose=165m   },
        new() { Symbol="HINDALCO",   StockName="Hindalco",           SectorName=sector, Pct1d= 1.60m, Pct5d= 3.00m, PERatio=12.6m, Price=685m,   PreviousClose=685m   },
        new() { Symbol="VEDL",       StockName="Vedanta",            SectorName=sector, Pct1d= 1.30m, Pct5d= 2.40m, PERatio=10.8m, Price=465m,   PreviousClose=465m   },
        new() { Symbol="SAIL",       StockName="SAIL",               SectorName=sector, Pct1d= 1.00m, Pct5d= 1.90m, PERatio=8.4m,  Price=125m,   PreviousClose=125m   },
        new() { Symbol="NATIONALUM", StockName="National Aluminium", SectorName=sector, Pct1d= 0.70m, Pct5d= 1.30m, PERatio=11.4m, Price=185m,   PreviousClose=185m   },
        new() { Symbol="NMDC",       StockName="NMDC",               SectorName=sector, Pct1d= 0.40m, Pct5d= 0.80m, PERatio=9.2m,  Price=68m,    PreviousClose=68m    },
        new() { Symbol="COALINDIA",  StockName="Coal India",         SectorName=sector, Pct1d= 0.20m, Pct5d= 0.40m, PERatio=7.4m,  Price=465m,   PreviousClose=465m   },
        new() { Symbol="GRASIM",     StockName="Grasim Industries",  SectorName=sector, Pct1d=-0.50m, Pct5d=-1.00m, PERatio=18.6m, Price=2680m,  PreviousClose=2680m  },
        new() { Symbol="MOIL",       StockName="MOIL",               SectorName=sector, Pct1d=-0.90m, Pct5d=-1.70m, PERatio=14.2m, Price=385m,   PreviousClose=385m   },
    },

            "Telecom" => new List<Stock>
    {
        new() { Symbol="BHARTIARTL", StockName="Bharti Airtel",      SectorName=sector, Pct1d= 2.40m, Pct5d= 4.50m, PERatio=44.8m, Price=1685m,  PreviousClose=1685m  },
        new() { Symbol="IDEA",       StockName="Vodafone Idea",      SectorName=sector, Pct1d= 1.80m, Pct5d= 3.30m, PERatio=0.0m,  Price=12m,    PreviousClose=12m    },
        new() { Symbol="TATACOMM",   StockName="Tata Communications",SectorName=sector, Pct1d= 1.40m, Pct5d= 2.60m, PERatio=28.6m, Price=1885m,  PreviousClose=1885m  },
        new() { Symbol="MTNL",       StockName="MTNL",               SectorName=sector, Pct1d= 1.00m, Pct5d= 1.90m, PERatio=0.0m,  Price=28m,    PreviousClose=28m    },
        new() { Symbol="HFCL",       StockName="HFCL",               SectorName=sector, Pct1d= 0.70m, Pct5d= 1.30m, PERatio=18.4m, Price=85m,    PreviousClose=85m    },
        new() { Symbol="STLTECH",    StockName="Sterlite Tech",      SectorName=sector, Pct1d= 0.50m, Pct5d= 0.90m, PERatio=22.6m, Price=185m,   PreviousClose=185m   },
        new() { Symbol="RAILTEL",    StockName="RailTel",            SectorName=sector, Pct1d= 0.30m, Pct5d= 0.60m, PERatio=28.4m, Price=485m,   PreviousClose=485m   },
        new() { Symbol="BSNL",       StockName="BSNL",               SectorName=sector, Pct1d= 0.10m, Pct5d= 0.20m, PERatio=0.0m,  Price=5m,     PreviousClose=5m     },
        new() { Symbol="TEJAS",      StockName="Tejas Networks",     SectorName=sector, Pct1d=-0.40m, Pct5d=-0.80m, PERatio=0.0m,  Price=985m,   PreviousClose=985m   },
        new() { Symbol="NELCO",      StockName="NELCO",              SectorName=sector, Pct1d=-0.80m, Pct5d=-1.50m, PERatio=22.4m, Price=685m,   PreviousClose=685m   },
    },

            "FMCG" => new List<Stock>
    {
        new() { Symbol="HINDUNILVR", StockName="HUL",                SectorName=sector, Pct1d= 1.90m, Pct5d= 3.50m, PERatio=58.4m, Price=2485m,  PreviousClose=2485m  },
        new() { Symbol="ITC",        StockName="ITC",                SectorName=sector, Pct1d= 1.60m, Pct5d= 3.00m, PERatio=22.8m, Price=465m,   PreviousClose=465m   },
        new() { Symbol="NESTLEIND",  StockName="Nestlé India",       SectorName=sector, Pct1d= 1.30m, Pct5d= 2.40m, PERatio=64.2m, Price=2285m,  PreviousClose=2285m  },
        new() { Symbol="BRITANNIA",  StockName="Britannia",          SectorName=sector, Pct1d= 1.00m, Pct5d= 1.90m, PERatio=48.6m, Price=5480m,  PreviousClose=5480m  },
        new() { Symbol="DABUR",      StockName="Dabur India",        SectorName=sector, Pct1d= 0.80m, Pct5d= 1.50m, PERatio=42.6m, Price=585m,   PreviousClose=585m   },
        new() { Symbol="MARICO",     StockName="Marico",             SectorName=sector, Pct1d= 0.60m, Pct5d= 1.10m, PERatio=44.8m, Price=685m,   PreviousClose=685m   },
        new() { Symbol="GODREJCP",   StockName="Godrej Consumer",    SectorName=sector, Pct1d= 0.40m, Pct5d= 0.80m, PERatio=38.4m, Price=1285m,  PreviousClose=1285m  },
        new() { Symbol="EMAMILTD",   StockName="Emami",              SectorName=sector, Pct1d= 0.20m, Pct5d= 0.40m, PERatio=32.4m, Price=685m,   PreviousClose=685m   },
        new() { Symbol="TATACONSUM", StockName="Tata Consumer",      SectorName=sector, Pct1d=-0.30m, Pct5d=-0.60m, PERatio=62.4m, Price=985m,   PreviousClose=985m   },
        new() { Symbol="VBL",        StockName="Varun Beverages",    SectorName=sector, Pct1d=-0.80m, Pct5d=-1.50m, PERatio=44.6m, Price=1685m,  PreviousClose=1685m  },
    },

            // Unknown sector → return empty list
            _ => new List<Stock>()
        };

    }
}


