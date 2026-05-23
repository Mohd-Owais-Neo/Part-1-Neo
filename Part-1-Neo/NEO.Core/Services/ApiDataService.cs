using NEO.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NEO.Core.Services
{
    public class ApiDataService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string BaseUrl = "https://www.alphavantage.co/query";

        public ApiDataService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        // =============================================
        // TEST API CONNECTION — Using GLOBAL_QUOTE
        // =============================================
        public async Task<bool> TestApiConnectionAsync()
        {
            try
            {
                Console.WriteLine("   → Testing Alpha Vantage API connection...");

                // Use GLOBAL_QUOTE on MSFT — works on free tier
                var url = $"{BaseUrl}?function=GLOBAL_QUOTE&symbol=MSFT&apikey={_apiKey}";
                var response = await _httpClient.GetStringAsync(url);

                Console.WriteLine($"   → Raw response length: {response.Length} chars");

                var json = JsonDocument.Parse(response);

                // Rate limit hit
                if (json.RootElement.TryGetProperty("Note", out var note))
                {
                    Console.WriteLine($"   ⚠️ Rate limit: {note.GetString()}");
                    Console.WriteLine("   → Waiting 60 seconds...");
                    await Task.Delay(60000);
                    return await TestApiConnectionAsync();
                }

                if (json.RootElement.TryGetProperty("Information", out var info))
                {
                    Console.WriteLine($"   ⚠️ API Info: {info.GetString()}");
                    return false;
                }

                if (json.RootElement.TryGetProperty("Error Message", out var error))
                {
                    Console.WriteLine($"   ❌ API Error: {error.GetString()}");
                    return false;
                }

                if (json.RootElement.TryGetProperty("Global Quote", out var quote))
                {
                    var symbol = quote.TryGetProperty("01. symbol", out var s)
                        ? s.GetString() : "?";
                    Console.WriteLine($"   ✅ API connection successful! Got quote for: {symbol}");
                    return true;
                }

                Console.WriteLine($"   ❌ Unexpected response: {response.Substring(0, Math.Min(200, response.Length))}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ API connection failed: {ex.Message}");
                return false;
            }
        }

        // =============================================
        // FETCH SECTOR PERFORMANCE
        // Calculated from top stock quotes per sector
        // =============================================
        public async Task<List<Sector>> FetchUSSectorPerformanceAsync()
        {
            var result = new List<Sector>();

            Console.WriteLine("   → Fetching sector performance via stock quotes...");

            // Representative stock per sector (1 per sector to save API calls)
            var sectorRepresentatives = new Dictionary<string, string>
            {
                { "Technology",               "MSFT"  },
                { "Health Care",              "JNJ"   },
                { "Industrials",              "HON"   },
                { "Energy",                   "XOM"   },
                { "Materials",                "LIN"   },
                { "Consumer Discretionary",   "AMZN"  },
                { "Consumer Staples",         "PG"    },
                { "Utilities",                "NEE"   },
                { "Real Estate",              "AMT"   },
                { "Communication Services",   "GOOGL" }
            };

            int rank = 1;
            foreach (var kvp in sectorRepresentatives)
            {
                try
                {
                    Console.WriteLine($"   → Fetching {kvp.Key} ({kvp.Value})...");

                    var url = $"{BaseUrl}?function=GLOBAL_QUOTE&symbol={kvp.Value}&apikey={_apiKey}";
                    var response = await _httpClient.GetStringAsync(url);
                    var json = JsonDocument.Parse(response);

                    // Handle rate limit
                    if (json.RootElement.TryGetProperty("Note", out _))
                    {
                        Console.WriteLine("   ⚠️ Rate limit hit — waiting 60 seconds...");
                        await Task.Delay(60000);
                        response = await _httpClient.GetStringAsync(url);
                        json = JsonDocument.Parse(response);
                    }

                    if (!json.RootElement.TryGetProperty("Global Quote", out var quote))
                    {
                        Console.WriteLine($"   ⚠️ No quote for {kvp.Value} — skipping");
                        continue;
                    }

                    var pctStr = quote.TryGetProperty("10. change percent", out var p)
                        ? p.GetString() ?? "0%" : "0%";
                    var pct = ParsePercent(pctStr);

                    result.Add(new Sector
                    {
                        SectorName = kvp.Key,
                        PctChange = pct,
                        Pct1d = pct,
                        Rank = rank++
                    });

                    Console.WriteLine($"   ✅ {kvp.Key}: {pct}%");

                    // Delay between calls — free tier allows 5 calls/min
                    await Task.Delay(13000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ Error fetching {kvp.Key}: {ex.Message}");
                }
            }

            // Sort by performance descending and re-rank
            result = result.OrderByDescending(s => s.PctChange).ToList();
            for (int i = 0; i < result.Count; i++)
                result[i].Rank = i + 1;

            Console.WriteLine($"   ✅ Total sectors fetched: {result.Count}");
            return result;
        }

        // =============================================
        // FETCH TOP STOCKS FOR A SECTOR
        // =============================================
        public async Task<List<Stock>> FetchTopStocksForSectorAsync(
            string sectorName, int topN = 5)
        {
            var result = new List<Stock>();
            var symbols = GetSectorSymbols(sectorName);

            foreach (var symbol in symbols.Take(topN))
            {
                try
                {
                    Console.WriteLine($"   → Fetching stock: {symbol}...");

                    var url = $"{BaseUrl}?function=GLOBAL_QUOTE&symbol={symbol}&apikey={_apiKey}";
                    var response = await _httpClient.GetStringAsync(url);
                    var json = JsonDocument.Parse(response);

                    if (!json.RootElement.TryGetProperty("Global Quote", out var quote))
                        continue;

                    result.Add(new Stock
                    {
                        Symbol = symbol,
                        StockName = symbol,
                        SectorName = sectorName,
                        PreviousClose = ParseDecimal(quote, "08. previous close"),
                        Pct1d = ParsePercent(
                            quote.TryGetProperty("10. change percent", out var p)
                            ? p.GetString() ?? "0%" : "0%")
                    });

                    Console.WriteLine($"   ✅ {symbol} fetched");
                    await Task.Delay(13000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ Error fetching {symbol}: {ex.Message}");
                }
            }

            return result;
        }

        // =============================================
        // HELPER METHODS
        // =============================================
        private decimal ParsePercent(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            value = value.Replace("%", "").Trim();
            return decimal.TryParse(value, out var result) ? result : 0;
        }

        private decimal ParseDecimal(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                var str = prop.GetString() ?? "0";
                return decimal.TryParse(str, out var result) ? result : 0;
            }
            return 0;
        }

        private List<string> GetSectorSymbols(string sectorName)
        {
            var map = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase)
            {
                { "Technology",               new List<string> { "AAPL","MSFT","NVDA","GOOGL","META" } },
                { "Energy",                   new List<string> { "XOM","CVX","COP","SLB","EOG"        } },
                { "Health Care",              new List<string> { "JNJ","UNH","PFE","MRK","ABBV"       } },
                { "Consumer Discretionary",   new List<string> { "AMZN","TSLA","HD","MCD","NKE"       } },
                { "Consumer Staples",         new List<string> { "PG","KO","PEP","WMT","COST"         } },
                { "Industrials",              new List<string> { "HON","UPS","CAT","BA","GE"           } },
                { "Utilities",                new List<string> { "NEE","DUK","SO","D","AEP"            } },
                { "Real Estate",              new List<string> { "AMT","PLD","CCI","EQIX","PSA"        } },
                { "Materials",                new List<string> { "LIN","APD","ECL","DD","NEM"          } },
                { "Communication Services",   new List<string> { "GOOGL","META","DIS","NFLX","T"       } }
            };

            return map.ContainsKey(sectorName) ? map[sectorName] : new List<string>();
        }
    }
}