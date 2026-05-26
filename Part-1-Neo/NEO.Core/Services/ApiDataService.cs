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
        // TEST API CONNECTION
        // =============================================
        public async Task<bool> TestApiConnectionAsync()
        {
            try
            {
                Console.WriteLine("   → Testing Alpha Vantage API connection...");
                var url = $"{BaseUrl}?function=GLOBAL_QUOTE&symbol=MSFT&apikey={_apiKey}";
                var response = await _httpClient.GetStringAsync(url);

                // Always print raw response so we can see what's coming back
                Console.WriteLine($"   → Response length: {response.Length} chars");
                Console.WriteLine($"   → Raw: {response.Substring(0, Math.Min(300, response.Length))}");

                var json = JsonDocument.Parse(response);

                // Rate limit
                if (json.RootElement.TryGetProperty("Note", out var note))
                {
                    Console.WriteLine($"   ⚠️ Rate limit — waiting 65 seconds...");
                    await Task.Delay(65000);
                    return await TestApiConnectionAsync();
                }

                // Info message (daily limit hit)
                if (json.RootElement.TryGetProperty("Information", out var info))
                {
                    Console.WriteLine($"   ⚠️ API Info: {info.GetString()}");
                    Console.WriteLine($"   → This means daily API limit reached (25 calls/day on free tier)");
                    Console.WriteLine($"   → Bypassing API test — assuming connected...");
                    return true; // bypass for now
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

                // Print all keys for debugging
                Console.WriteLine("   → Keys in response:");
                foreach (var prop in json.RootElement.EnumerateObject())
                    Console.WriteLine($"      • {prop.Name}");

                // If Global Quote is missing but no error → still return true
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
        // FETCH SECTOR PERFORMANCE — Any Market
        // market = "US" | "INDIA" | "CHINA"
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
                    });

                    Console.WriteLine($"   ✅ [{market}] {kvp.Key}: {pct}%");

                    // Free tier: 5 calls/min → wait 13 seconds
                    await Task.Delay(13000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ Error fetching {kvp.Key}: {ex.Message}");
                }
            }

            // Sort by performance descending and rank
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

                    result.Add(new Stock
                    {
                        Symbol = symbol,
                        StockName = symbol,
                        SectorName = sectorName,
                        PreviousClose = ParseDecimal(quote, "08. previous close"),
                        Pct1d = ParsePercent(pctChange)
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
        // 1 stock per sector per market
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

                // Indian companies listed on US exchanges (ADR)
                "INDIA" => new Dictionary<string, string>
        {
            { "Technology",  "INFY"  },   // Infosys
            { "Pharma",      "RDY"   },   // Dr Reddy's
            { "Auto",        "TTM"   },   // Tata Motors
            { "Metals",      "VEDL"  },   // Vedanta
            { "Consumer",    "HDB"   },   // HDFC Bank
            { "Industrials", "WIT"   },   // Wipro
            { "Energy",      "SLB"   },   // Use SLB as proxy
            { "FMCG",        "UL"    }    // Unilever as proxy
        },

                // Chinese companies listed on US exchanges (ADR)
                "CHINA" => new Dictionary<string, string>
        {
            { "Technology",  "BIDU"  },   // Baidu
            { "Consumer",    "JD"    },   // JD.com
            { "Industrials", "YUMC"  },   // Yum China
            { "Health Care", "BGNE"  },   // BeiGene
            { "Auto",        "NIO"   },   // NIO
            { "Telecom",     "CHL"   },   // China Mobile
            { "Energy",      "CEO"   },   // CNOOC
            { "Materials",   "ACH"   }    // Aluminum Corp
        },

                _ => new Dictionary<string, string>()
            };
        }

        // =============================================
        // SECTOR STOCK SYMBOLS — For Stock Filtering
        // =============================================
        private Dictionary<string, List<string>> GetSectorStockSymbols(
            string sectorName, string market)
        {
            if (market.ToUpper() == "INDIA")
            {
                return new Dictionary<string, List<string>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    { "Technology", new List<string>
                        { "INFY.BSE","TCS.BSE","WIPRO.BSE","HCLTECH.BSE","TECHM.BSE" }},
                    { "Pharma",     new List<string>
                        { "SUNPHARMA.BSE","DRREDDY.BSE","CIPLA.BSE","DIVISLAB.BSE","APOLLOHOSP.BSE" }},
                    { "Auto",       new List<string>
                        { "TATAMOTORS.BSE","MARUTI.BSE","M&M.BSE","BAJAJ-AUTO.BSE","HEROMOTOCO.BSE" }},
                    { "Energy",     new List<string>
                        { "RELIANCE.BSE","ONGC.BSE","BPCL.BSE","IOC.BSE","POWERGRID.BSE" }},
                    { "Metals",     new List<string>
                        { "TATASTEEL.BSE","HINDALCO.BSE","JSWSTEEL.BSE","COALINDIA.BSE","VEDL.BSE" }}
                };
            }

            // Default US
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

        private List<string> GetSectorSymbols(string sectorName)
            => GetSectorStockSymbols(sectorName, "US")
               .GetValueOrDefault(sectorName, new List<string>());

        // =============================================
        // HELPERS
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
    }
}