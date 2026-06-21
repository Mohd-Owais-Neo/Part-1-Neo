using NEO.Core.Data;
using NEO.Core.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Services
{
    public class MarketDataService
    {
        private readonly HttpClient _httpClient;
        private readonly DatabaseHelper _db;

        public MarketDataService(DatabaseHelper db)
        {
            _db = db;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(25);

            // Browser-like headers to reduce Yahoo blocking
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");

            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://finance.yahoo.com/");
        }

        // =============================================
        // MAIN METHOD — fetch from Yahoo, fallback to local seed
        // =============================================
        public async Task<List<Stock>> GetStocksFromYahooAsync()
        {
            var allStocks = new List<Stock>();

            // Small starter universe with sectors needed by your pipeline
            var symbols = new List<string>
            {
                "TCS.NS",
                "INFY.NS",
                "RELIANCE.NS",
                "HDFCBANK.NS",
                "ITC.NS",
                "BHARTIARTL.NS",
                "ZOMATO.NS",
                "NTPC.NS",
                "POWERGRID.NS",
                "TATAPOWER.NS",
                "TITAN.NS",
                "MARUTI.NS",
                "TATAMOTORS.NS"
            };

            // batch size kept small to reduce 429 risk
            const int batchSize = 4;

            for (int i = 0; i < symbols.Count; i += batchSize)
            {
                var batch = symbols.Skip(i).Take(batchSize).ToList();
                var batchStocks = await FetchBatchWithRetryAsync(batch);
                allStocks.AddRange(batchStocks);

                // short delay between batches
                await Task.Delay(1500);
            }

            // If Yahoo completely fails, use local fallback seed data
            if (allStocks.Count == 0)
            {
                Console.WriteLine("   ⚠️ Yahoo failed completely — using local fallback stock seed");
                allStocks = GetFallbackSeedStocks();
            }

            // remove duplicates by symbol
            allStocks = allStocks
                .GroupBy(s => s.Symbol)
                .Select(g => g.First())
                .ToList();

            return allStocks;
        }

        // =============================================
        // FETCH ONE BATCH WITH RETRY
        // =============================================
        private async Task<List<Stock>> FetchBatchWithRetryAsync(List<string> symbols)
        {
            var result = new List<Stock>();

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    string symbolCsv = string.Join(",", symbols);
                    string url = $"https://query1.finance.yahoo.com/v7/finance/quote?symbols={symbolCsv}";

                    using var response = await _httpClient.GetAsync(url);

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        Console.WriteLine($"   ⚠️ Yahoo 429 on batch [{symbolCsv}] — attempt {attempt}/3");
                        await Task.Delay(2000 * attempt);
                        continue;
                    }

                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    dynamic? data = JsonConvert.DeserializeObject(json);

                    var items = data?.quoteResponse?.result;
                    if (items == null)
                        return result;

                    foreach (var item in items)
                    {
                        string rawSymbol = item.symbol?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(rawSymbol))
                            continue;

                        var stock = new Stock
                        {
                            Symbol = NormalizeSymbol(rawSymbol),
                            StockName = item.shortName?.ToString() ?? NormalizeSymbol(rawSymbol),
                            SectorName = MapSector(rawSymbol),
                            PreviousClose = ToDecimal(item.regularMarketPreviousClose),
                            Price = ToDecimal(item.regularMarketPrice),
                            Pct1d = ToDecimal(item.regularMarketChangePercent),
                            Pct5d = 0m,
                            Pct20d = 0m,
                            MarketCap = ToDecimal(item.marketCap),
                            PERatio = ToDecimal(item.trailingPE),
                            AvgTurnover30d = 0m
                        };

                        if (stock.PreviousClose <= 0 && stock.Price > 0)
                            stock.PreviousClose = stock.Price;

                        result.Add(stock);
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ Yahoo batch failed (attempt {attempt}/3): {ex.Message}");
                    await Task.Delay(2000 * attempt);
                }
            }

            return result;
        }

        // =============================================
        // NORMALIZE SYMBOL
        // Example: TCS.NS -> TCS
        // =============================================
        private string NormalizeSymbol(string rawSymbol)
        {
            return rawSymbol.Replace(".NS", "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        // =============================================
        // MAP SYMBOL TO SECTOR
        // =============================================
        private string MapSector(string symbol)
        {
            return symbol.ToUpperInvariant() switch
            {
                "TCS.NS" => "Technology",
                "INFY.NS" => "Technology",

                "RELIANCE.NS" => "Energy",

                "ITC.NS" => "Consumer Staples",

                "BHARTIARTL.NS" => "Communication Services",
                "ZOMATO.NS" => "Communication Services",

                "NTPC.NS" => "Utilities",
                "POWERGRID.NS" => "Utilities",
                "TATAPOWER.NS" => "Utilities",

                "TITAN.NS" => "Consumer Discretionary",
                "MARUTI.NS" => "Consumer Discretionary",
                "TATAMOTORS.NS" => "Consumer Discretionary",

                "HDFCBANK.NS" => "Consumer",

                _ => "Unknown"
            };
        }

        // =============================================
        // SAFE DECIMAL PARSER
        // =============================================
        private decimal ToDecimal(dynamic? value)
        {
            try
            {
                if (value == null) return 0m;
                return Convert.ToDecimal(value);
            }
            catch
            {
                return 0m;
            }
        }

        // =============================================
        // LOCAL FALLBACK SEED DATA
        // This ensures Table_1_All_Stocks is never empty
        // =============================================
        private List<Stock> GetFallbackSeedStocks()
        {
            return new List<Stock>
            {
                new() { Symbol="TCS",         StockName="TCS",                SectorName="Technology",              PreviousClose=3985m, Price=3985m, Pct1d=2.80m, Pct5d=5.20m, PERatio=28.4m, MarketCap=0m, AvgTurnover30d=0m },
                new() { Symbol="INFY",        StockName="Infosys",            SectorName="Technology",              PreviousClose=1685m, Price=1685m, Pct1d=2.20m, Pct5d=4.10m, PERatio=24.6m, MarketCap=0m, AvgTurnover30d=0m },

                new() { Symbol="RELIANCE",    StockName="Reliance Industries",SectorName="Energy",                  PreviousClose=2985m, Price=2985m, Pct1d=2.50m, Pct5d=4.80m, PERatio=24.6m, MarketCap=0m, AvgTurnover30d=0m },

                new() { Symbol="ITC",         StockName="ITC",                SectorName="Consumer Staples",        PreviousClose=465m,  Price=465m,  Pct1d=0.30m, Pct5d=0.60m, PERatio=22.8m, MarketCap=0m, AvgTurnover30d=0m },

                new() { Symbol="BHARTIARTL",  StockName="Bharti Airtel",      SectorName="Communication Services",  PreviousClose=1685m, Price=1685m, Pct1d=2.40m, Pct5d=4.50m, PERatio=44.8m, MarketCap=0m, AvgTurnover30d=0m },
                new() { Symbol="ZOMATO",      StockName="Zomato",             SectorName="Communication Services",  PreviousClose=245m,  Price=245m,  Pct1d=1.80m, Pct5d=3.30m, PERatio=0m,    MarketCap=0m, AvgTurnover30d=0m },

                new() { Symbol="NTPC",        StockName="NTPC",               SectorName="Utilities",               PreviousClose=368m,  Price=368m,  Pct1d=1.80m, Pct5d=3.30m, PERatio=14.8m, MarketCap=0m, AvgTurnover30d=0m },
                new() { Symbol="POWERGRID",   StockName="Power Grid",         SectorName="Utilities",               PreviousClose=325m,  Price=325m,  Pct1d=1.40m, Pct5d=2.60m, PERatio=16.4m, MarketCap=0m, AvgTurnover30d=0m },
                new() { Symbol="TATAPOWER",   StockName="Tata Power",         SectorName="Utilities",               PreviousClose=425m,  Price=425m,  Pct1d=1.10m, Pct5d=2.00m, PERatio=28.6m, MarketCap=0m, AvgTurnover30d=0m },

                new() { Symbol="TITAN",       StockName="Titan Company",      SectorName="Consumer Discretionary",  PreviousClose=3520m, Price=3520m, Pct1d=3.20m, Pct5d=5.80m, PERatio=44.2m, MarketCap=0m, AvgTurnover30d=0m },
                new() { Symbol="MARUTI",      StockName="Maruti Suzuki",      SectorName="Consumer Discretionary",  PreviousClose=12450m,Price=12450m,Pct1d=2.50m, Pct5d=4.60m, PERatio=24.8m, MarketCap=0m, AvgTurnover30d=0m },
                new() { Symbol="TATAMOTORS",  StockName="Tata Motors",        SectorName="Consumer Discretionary",  PreviousClose=965m,  Price=965m,  Pct1d=2.10m, Pct5d=3.90m, PERatio=10.4m, MarketCap=0m, AvgTurnover30d=0m },

                new() { Symbol="HDFCBANK",    StockName="HDFC Bank",          SectorName="Consumer",                PreviousClose=1725m, Price=1725m, Pct1d=1.00m, Pct5d=1.80m, PERatio=20m,   MarketCap=0m, AvgTurnover30d=0m }
            };
        }

        // =============================================
        // OPTIONAL helper if you want direct save usage later
        // =============================================
        public async Task SaveStocksToDatabase(List<Stock> stocks)
        {
            string runId = Guid.NewGuid().ToString();
            DateTime businessDate = DateTime.UtcNow.Date;

            await _db.InsertAllStocksAsync(runId, businessDate, stocks);
        }
    }
}
