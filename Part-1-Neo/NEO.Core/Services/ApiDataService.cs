using NEO.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

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
        // TEST API CONNECTION
        // =============================================
        public async Task<bool> TestApiConnectionAsync()
        {
            try
            {
                Console.WriteLine("   → Testing Alpha Vantage API connection...");
                var url = $"{BaseUrl}?function=GLOBAL_QUOTE&symbol=MSFT&apikey={_apiKey}";
                var response = await _httpClient.GetStringAsync(url);

                Console.WriteLine($"   → Response length: {response.Length} chars");
                Console.WriteLine($"   → Raw: {response.Substring(0, Math.Min(300, response.Length))}");

                var json = JsonDocument.Parse(response);

                if (json.RootElement.TryGetProperty("Note", out _))
                {
                    Console.WriteLine("   ⚠️ Rate limit — waiting 65 seconds...");
                    await Task.Delay(65000);
                    return await TestApiConnectionAsync();
                }

                if (json.RootElement.TryGetProperty("Information", out var info))
                {
                    Console.WriteLine($"   ⚠️ API Info: {info.GetString()}");
                    Console.WriteLine("   → This means daily API limit reached (25 calls/day on free tier)");
                    Console.WriteLine("   → Bypassing API test - assuming connected...");
                    return true;
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

                Console.WriteLine("   ⚠️ Unexpected format but no error — continuing...");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ API connection failed: {ex.Message}");
                return false;
            }
        }

        // =============================================
        // FETCH SINGLE STOCK QUOTE — Real Data
        // =============================================
        public async Task<StockQuote?> FetchStockQuoteAsync(string symbol)
        {
            try
            {
                var url = $"{BaseUrl}?function=GLOBAL_QUOTE&symbol={symbol}&apikey={_apiKey}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response);

                if (json.RootElement.TryGetProperty("Information", out _) ||
                    json.RootElement.TryGetProperty("Note", out _))
                {
                    Console.WriteLine($"   ⚠️ [{symbol}] API rate limit hit");
                    return null;
                }

                if (!json.RootElement.TryGetProperty("Global Quote", out var quote))
                    return null;

                var priceStr = GetJsonString(quote, "05. price");
                var prevStr = GetJsonString(quote, "08. previous close");

                if (string.IsNullOrEmpty(priceStr)) return null;

                var price = decimal.Parse(priceStr,
                                    System.Globalization.CultureInfo.InvariantCulture);
                var prevClose = decimal.Parse(prevStr,
                                    System.Globalization.CultureInfo.InvariantCulture);
                var pct1d = prevClose > 0
                                ? Math.Round((price - prevClose) / prevClose * 100, 4)
                                : 0m;

                return new StockQuote
                {
                    Symbol = symbol,
                    Price = price,
                    PrevClose = prevClose,
                    Pct1d = pct1d
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ [{symbol}] Error: {ex.Message}");
                return null;
            }
        }

        // =============================================
        // FETCH SECTOR PERFORMANCE — Any Market
        // =============================================
        public async Task<List<Sector>> FetchSectorPerformanceAsync(string market)
        {
            var result = new List<Sector>();
            var representatives = GetMarketRepresentatives(market);

            Console.WriteLine($"   → Fetching {market} sector performance...");

            foreach (var kvp in representatives)
            {
                try
                {
                    Console.WriteLine($"   → [{market}] {kvp.Key} ({kvp.Value})...");

                    var url = $"{BaseUrl}?function=GLOBAL_QUOTE&symbol={kvp.Value}&apikey={_apiKey}";
                    var response = await _httpClient.GetStringAsync(url);
                    var json = JsonDocument.Parse(response);

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
                        Pct1d = pct
                    });

                    Console.WriteLine($"   ✅ [{market}] {kvp.Key}: {pct}%");
                    await Task.Delay(13000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ Error fetching {kvp.Key}: {ex.Message}");
                }
            }

            result = result.OrderByDescending(s => s.PctChange).ToList();
            for (int i = 0; i < result.Count; i++)
                result[i].Rank = i + 1;

            Console.WriteLine($"   ✅ {market} total sectors fetched: {result.Count}");
            return result;
        }

        // =============================================
        // FETCH TOP STOCKS FOR A SECTOR
        // =============================================
        public async Task<List<Stock>> FetchTopStocksForSectorAsync(
            string sectorName, string market = "US", int topN = 5)
        {
            var result = new List<Stock>();
            var symbolMap = GetSectorStockSymbols(sectorName, market);
            var symbols = symbolMap.ContainsKey(sectorName)
                            ? symbolMap[sectorName]
                            : new List<string>();

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

                    var pctChange = "0%";
                    if (quote.TryGetProperty("10. change percent", out var p))
                        pctChange = p.GetString() ?? "0%";

                    var price = ParseDecimal(quote, "05. price");

                    result.Add(new Stock
                    {
                        Symbol = symbol,
                        StockName = symbol,
                        SectorName = sectorName,
                        PreviousClose = ParseDecimal(quote, "08. previous close"),
                        Pct1d = ParsePercent(pctChange),
                        Price = price
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
        // MARKET REPRESENTATIVES
        // =============================================
        private Dictionary<string, string> GetMarketRepresentatives(string market)
        {
            return market.ToUpper() switch
            {
                "US" => new Dictionary<string, string>
                {
                    { "Technology",             "MSFT"  },
                    { "Health Care",            "JNJ"   },
                    { "Industrials",            "HON"   },
                    { "Energy",                 "XOM"   },
                    { "Materials",              "LIN"   },
                    { "Consumer Discretionary", "AMZN"  },
                    { "Consumer Staples",       "PG"    },
                    { "Utilities",              "NEE"   },
                    { "Real Estate",            "AMT"   },
                    { "Communication Services", "GOOGL" }
                },
                "INDIA" => new Dictionary<string, string>
                {
                    { "Technology",  "INFY" },
                    { "Pharma",      "RDY"  },
                    { "Auto",        "TTM"  },
                    { "Metals",      "VEDL" },
                    { "Consumer",    "HDB"  },
                    { "Industrials", "WIT"  },
                    { "Energy",      "SLB"  },
                    { "FMCG",        "UL"   }
                },
                "CHINA" => new Dictionary<string, string>
                {
                    { "Technology",  "BIDU" },
                    { "Consumer",    "JD"   },
                    { "Industrials", "YUMC" },
                    { "Health Care", "BGNE" },
                    { "Auto",        "NIO"  },
                    { "Telecom",     "CHL"  },
                    { "Energy",      "CEO"  },
                    { "Materials",   "ACH"  }
                },
                _ => new Dictionary<string, string>()
            };
        }

        // =============================================
        // SECTOR STOCK SYMBOLS
        // =============================================
        private Dictionary<string, List<string>> GetSectorStockSymbols(
            string sectorName, string market)
        {
            if (market.ToUpper() == "INDIA")
            {
                return new Dictionary<string, List<string>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    { "Technology", new List<string> { "INFY","WIT","HDB","RDY","TTM"         }},
                    { "Pharma",     new List<string> { "RDY","CIPLA.NS","SUNPHARMA.NS"         }},
                    { "Auto",       new List<string> { "TTM","HMC","TM"                        }},
                    { "Energy",     new List<string> { "SLB","XOM","CVX"                       }},
                    { "Metals",     new List<string> { "VEDL","FCX","NEM"                      }}
                };
            }

            return new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase)
            {
                { "Technology",             new List<string> { "AAPL","MSFT","NVDA","GOOGL","META" }},
                { "Energy",                 new List<string> { "XOM","CVX","COP","SLB","EOG"       }},
                { "Health Care",            new List<string> { "JNJ","UNH","PFE","MRK","ABBV"      }},
                { "Consumer Discretionary", new List<string> { "AMZN","TSLA","HD","MCD","NKE"      }},
                { "Consumer Staples",       new List<string> { "PG","KO","PEP","WMT","COST"        }},
                { "Industrials",            new List<string> { "HON","UPS","CAT","BA","GE"         }},
                { "Utilities",              new List<string> { "NEE","DUK","SO","D","AEP"          }},
                { "Real Estate",            new List<string> { "AMT","PLD","CCI","EQIX","PSA"      }},
                { "Materials",              new List<string> { "LIN","APD","ECL","DD","NEM"        }},
                { "Communication Services", new List<string> { "GOOGL","META","DIS","NFLX","T"     }}
            };
        }

        // =============================================
        // HELPERS
        // =============================================
        private string GetJsonString(JsonElement element, string key)
        {
            if (element.TryGetProperty(key, out var val))
                return val.GetString() ?? "";
            return "";
        }

        private decimal ParsePercent(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            value = value.Replace("%", "").Trim();
            return decimal.TryParse(value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var result) ? result : 0;
        }

        private decimal ParseDecimal(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                var str = prop.GetString() ?? "0";
                return decimal.TryParse(str,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var result) ? result : 0;
            }
            return 0;
        }
    }
}