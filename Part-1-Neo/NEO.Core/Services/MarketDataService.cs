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
using System.IO;

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

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");

            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://finance.yahoo.com/");
        }

        // =============================================
        // MAIN METHOD — Yahoo first, fallback second
        // =============================================
        public async Task<List<Stock>> GetStocksFromYahooAsync()
        {
            Console.WriteLine("   → Loading NSE EQ universe from official NSE CSV...");

            var nseSymbols = await LoadNseEqUniverseAsync(maxCount: 500);

            if (nseSymbols.Count == 0)
            {
                throw new Exception(
                    "NSE universe returned 0 symbols. Local CSV was not found/published or online NSE CSV failed.");
            }

            Console.WriteLine($"   ✅ NSE EQ universe loaded: {nseSymbols.Count} symbols");

            var yahooSymbols = nseSymbols
                .Select(s => $"{s.Symbol}.NS")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var allStocks = new List<Stock>();

            const int batchSize = 50;

            for (int i = 0; i < yahooSymbols.Count; i += batchSize)
            {
                var batch = yahooSymbols.Skip(i).Take(batchSize).ToList();

                Console.WriteLine($"   → Yahoo batch {i / batchSize + 1}: {batch.Count} symbols");

                var batchStocks = await FetchNseBatchWithRetryAsync(batch, nseSymbols);
                allStocks.AddRange(batchStocks);

                await Task.Delay(1000);
            }

            allStocks = allStocks
                .Where(s => !string.IsNullOrWhiteSpace(s.Symbol))
                .GroupBy(s => s.Symbol)
                .Select(g => g.First())
                .ToList();

            if (allStocks.Count == 0)
            {
                throw new Exception(
                    $"NSE universe loaded {nseSymbols.Count} symbols, but Yahoo returned 0 usable quote rows. " +
                    "Yahoo quote fetch is failing from Azure.");
            }

            Console.WriteLine($"   ✅ MarketDataService returned {allStocks.Count} stocks");

            return allStocks;
        }

        private async Task<List<NseSecurity>> LoadNseEqUniverseAsync(int maxCount)
        {
            try
            {
                var possiblePaths = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "nse_eq_symbols.csv"),
            Path.Combine(Directory.GetCurrentDirectory(), "nse_eq_symbols.csv")
        };

                var home = Environment.GetEnvironmentVariable("HOME");

                if (!string.IsNullOrWhiteSpace(home))
                {
                    possiblePaths.Add(Path.Combine(home, "site", "wwwroot", "nse_eq_symbols.csv"));
                }

                foreach (var path in possiblePaths)
                {
                    Console.WriteLine($"   → Checking NSE CSV path: {path}");

                    if (File.Exists(path))
                    {
                        Console.WriteLine($"   ✅ Local NSE CSV found: {path}");

                        var csv = await File.ReadAllTextAsync(path);
                        var localResult = ParseNseCsv(csv, maxCount);

                        Console.WriteLine($"   ✅ Local NSE EQ universe loaded: {localResult.Count} symbols");

                        if (localResult.Count > 0)
                            return localResult;
                    }
                }

                Console.WriteLine("   ⚠️ Local NSE CSV not found in any expected path.");
                Console.WriteLine("   → Trying NSE online CSV...");

                var url = "https://nsearchives.nseindia.com/content/equities/sec_list.csv";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                request.Headers.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                    "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");

                request.Headers.Referrer = new Uri("https://www.nseindia.com/");
                request.Headers.Accept.ParseAdd("text/csv,*/*");

                using var response = await _httpClient.SendAsync(request);

                Console.WriteLine($"   → NSE online CSV status: {(int)response.StatusCode} {response.StatusCode}");

                response.EnsureSuccessStatusCode();

                var onlineCsv = await response.Content.ReadAsStringAsync();
                var onlineResult = ParseNseCsv(onlineCsv, maxCount);

                Console.WriteLine($"   ✅ Online NSE EQ universe loaded: {onlineResult.Count} symbols");

                return onlineResult;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ NSE universe load failed: {ex.Message}");
                return new List<NseSecurity>();
            }
        }

        private List<NseSecurity> ParseNseCsv(string csv, int maxCount)
        {
            var result = new List<NseSecurity>();

            var lines = csv
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToList();

            if (lines.Count <= 1)
                return result;

            foreach (var line in lines.Skip(1))
            {
                var cols = SplitCsvLine(line);

                if (cols.Count < 3)
                    continue;

                var symbol = cols[0].Trim();
                var series = cols[1].Trim();
                var name = cols[2].Trim();

                if (!series.Equals("EQ", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(name))
                    continue;

                result.Add(new NseSecurity
                {
                    Symbol = symbol,
                    Series = series,
                    SecurityName = name
                });

                if (result.Count >= maxCount)
                    break;
            }

            return result;
        }

        private List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var current = "";
            bool inQuotes = false;

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    result.Add(current);
                    current = "";
                }
                else
                {
                    current += c;
                }
            }

            result.Add(current);

            return result;
        }

        // =============================================
        // 50+ NSE STOCK UNIVERSE
        // Excludes Banking, Finance, Insurance
        // =============================================
        private List<string> GetNseUniverse()
        {
            return new List<string>
            {
                // Technology
                "TCS.NS",
                "INFY.NS",
                "WIPRO.NS",
                "HCLTECH.NS",
                "TECHM.NS",
                "LTIM.NS",
                "MPHASIS.NS",
                "PERSISTENT.NS",
                "COFORGE.NS",
                "KPITTECH.NS",

                // Consumer Discretionary / Auto / Retail
                "TITAN.NS",
                "MARUTI.NS",
                "TATAMOTORS.NS",
                "BAJAJ-AUTO.NS",
                "HEROMOTOCO.NS",
                "EICHERMOT.NS",
                "M&M.NS",
                "TVSMOTOR.NS",
                "ASHOKLEY.NS",
                "BATAINDIA.NS",
                "PAGEIND.NS",
                "VOLTAS.NS",

                // Utilities / Power
                "NTPC.NS",
                "POWERGRID.NS",
                "TATAPOWER.NS",
                "ADANIGREEN.NS",
                "ADANIPOWER.NS",
                "TORNTPOWER.NS",
                "CESC.NS",
                "NHPC.NS",
                "SJVN.NS",

                // Communication Services / Internet / Telecom
                "BHARTIARTL.NS",
                "ZOMATO.NS",
                "INDIAMART.NS",
                "NAUKRI.NS",
                "POLICYBZR.NS",
                "DELHIVERY.NS",
                "IRCTC.NS",
                "IDEA.NS",
                "JUSTDIAL.NS",

                // Energy
                "RELIANCE.NS",
                "ONGC.NS",
                "IOC.NS",
                "BPCL.NS",
                "GAIL.NS",
                "HINDPETRO.NS",
                "OIL.NS",
                "PETRONET.NS",

                // Consumer Staples / FMCG
                "ITC.NS",
                "HINDUNILVR.NS",
                "NESTLEIND.NS",
                "BRITANNIA.NS",
                "DABUR.NS",
                "MARICO.NS",
                "GODREJCP.NS",
                "TATACONSUM.NS",
                "VBL.NS",
                "COLPAL.NS",

                // Pharma / Health Care
                "SUNPHARMA.NS",
                "DRREDDY.NS",
                "CIPLA.NS",
                "DIVISLAB.NS",
                "APOLLOHOSP.NS",
                "LUPIN.NS",
                "AUROPHARMA.NS",
                "BIOCON.NS",
                "ALKEM.NS",
                "TORNTPHARM.NS",

                // Industrials / Capital Goods
                "LT.NS",
                "SIEMENS.NS",
                "ABB.NS",
                "BHEL.NS",
                "HAL.NS",
                "CUMMINSIND.NS",
                "THERMAX.NS",
                "BEL.NS",

                // Materials / Metals / Cement
                "JSWSTEEL.NS",
                "TATASTEEL.NS",
                "HINDALCO.NS",
                "VEDL.NS",
                "SAIL.NS",
                "COALINDIA.NS",
                "NMDC.NS",
                "NATIONALUM.NS",
                "GRASIM.NS",
                "ULTRACEMCO.NS"
            };
        }

        // =============================================
        // FETCH ONE YAHOO BATCH
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
                        await Task.Delay(3000 * attempt);
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

                        string sector = MapSector(rawSymbol);

                        if (IsForbiddenSector(sector))
                            continue;

                        var stock = new Stock
                        {
                            Symbol = NormalizeSymbol(rawSymbol),
                            StockName = item.shortName?.ToString() ?? NormalizeSymbol(rawSymbol),
                            SectorName = sector,
                            PreviousClose = ToDecimal(item.regularMarketPreviousClose),
                            Price = ToDecimal(item.regularMarketPrice),
                            Pct1d = ToDecimal(item.regularMarketChangePercent),
                            Pct5d = 0m,
                            Pct20d = 0m,
                            MarketCap = ToDecimal(item.marketCap),
                            PERatio = ToDecimal(item.trailingPE),
                            AvgTurnover30d = ToDecimal(item.regularMarketVolume)
                        };

                        if (stock.PreviousClose <= 0 && stock.Price > 0)
                            stock.PreviousClose = stock.Price;

                        result.Add(stock);
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ Yahoo batch failed attempt {attempt}/3: {ex.Message}");
                    await Task.Delay(3000 * attempt);
                }
            }

            return result;
        }

        private async Task<List<Stock>> FetchNseBatchWithRetryAsync(
    List<string> yahooSymbols,
    List<NseSecurity> masterList)
        {
            var result = new List<Stock>();

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var symbolCsv = string.Join(",", yahooSymbols);
                    var url = $"https://query1.finance.yahoo.com/v7/finance/quote?symbols={symbolCsv}";

                    using var response = await _httpClient.GetAsync(url);

                    if ((int)response.StatusCode == 429)
                    {
                        Console.WriteLine($"   ⚠️ Yahoo 429 on batch — attempt {attempt}/3");
                        await Task.Delay(3000 * attempt);
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

                        var normalized = NormalizeSymbol(rawSymbol);

                        var master = masterList.FirstOrDefault(x =>
                            x.Symbol.Equals(normalized, StringComparison.OrdinalIgnoreCase));

                        var stockName = item.shortName?.ToString()
                                        ?? master?.SecurityName
                                        ?? normalized;

                        var sector = MapSectorFromText(normalized, stockName);

                        var currentPrice = ToDecimal(item.regularMarketPrice);
                        var previousClose = ToDecimal(item.regularMarketPreviousClose);
                        var pct1d = ToDecimal(item.regularMarketChangePercent);

                        if (previousClose <= 0 && currentPrice > 0)
                            previousClose = currentPrice;

                        var stock = new Stock
                        {
                            Symbol = normalized,
                            StockName = stockName,
                            SectorName = sector,
                            Price = currentPrice,
                            PreviousClose = previousClose,
                            Pct1d = pct1d,
                            Pct5d = 0m,
                            Pct20d = 0m,
                            MarketCap = ToDecimal(item.marketCap),
                            PERatio = ToDecimal(item.trailingPE),
                            AvgTurnover30d = ToDecimal(item.regularMarketVolume)
                        };

                        result.Add(stock);
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ Yahoo batch failed attempt {attempt}/3: {ex.Message}");

                    if (attempt < 3)
                        await Task.Delay(3000 * attempt);
                }
            }

            return result;
        }

        private string MapSectorFromText(string symbol, string stockName)
        {
            var text = $"{symbol} {stockName}".ToUpperInvariant();

            if (text.Contains("BANK") ||
                text.Contains("FINANCE") ||
                text.Contains("FINANCIAL") ||
                text.Contains("INSURANCE") ||
                text.Contains("SECURITIES") ||
                text.Contains("CAPITAL") ||
                text.Contains("INVESTMENT") ||
                text.Contains("AMC"))
                return "Finance";

            if (text.Contains("TECH") ||
                text.Contains("SOFT") ||
                text.Contains("INFOTECH") ||
                text.Contains("COMPUTER") ||
                text.Contains("DIGITAL") ||
                text.Contains("SYSTEMS"))
                return "Technology";

            if (text.Contains("PHARMA") ||
                text.Contains("DRUG") ||
                text.Contains("LAB") ||
                text.Contains("HEALTH") ||
                text.Contains("HOSPITAL") ||
                text.Contains("BIO"))
                return "Pharma";

            if (text.Contains("POWER") ||
                text.Contains("ENERGY") ||
                text.Contains("GREEN") ||
                text.Contains("ELECTRIC") ||
                text.Contains("GRID"))
                return "Utilities";

            if (text.Contains("STEEL") ||
                text.Contains("METAL") ||
                text.Contains("ALUMIN") ||
                text.Contains("CEMENT") ||
                text.Contains("MINERAL") ||
                text.Contains("CHEMICAL"))
                return "Materials";

            if (text.Contains("AUTO") ||
                text.Contains("MOTOR") ||
                text.Contains("TYRE") ||
                text.Contains("SUZUKI") ||
                text.Contains("TRACTOR") ||
                text.Contains("VEHICLE"))
                return "Consumer Discretionary";

            if (text.Contains("FOOD") ||
                text.Contains("CONSUMER") ||
                text.Contains("FMCG") ||
                text.Contains("BEVERAGE") ||
                text.Contains("RETAIL") ||
                text.Contains("TEXTILE"))
                return "Consumer Staples";

            if (text.Contains("OIL") ||
                text.Contains("GAS") ||
                text.Contains("PETRO") ||
                text.Contains("REFIN") ||
                text.Contains("COAL"))
                return "Energy";

            if (text.Contains("TELECOM") ||
                text.Contains("COMMUNICATION") ||
                text.Contains("MEDIA") ||
                text.Contains("INTERNET"))
                return "Communication Services";

            if (text.Contains("ENGINEERING") ||
                text.Contains("INDUSTR") ||
                text.Contains("INFRA") ||
                text.Contains("CONSTRUCTION") ||
                text.Contains("LOGISTICS"))
                return "Industrials";

            return "Unknown";
        }

        // =============================================
        // NORMALIZE SYMBOL
        // =============================================
        private string NormalizeSymbol(string rawSymbol)
        {
            return rawSymbol
                .Replace(".NS", "", StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        // =============================================
        // STRICT FORBIDDEN SECTOR CHECK
        // =============================================
        private bool IsForbiddenSector(string sectorName)
        {
            var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Banking",
                "Finance",
                "Insurance",
                "Entertainment",
                "Alcohol",
                "Gambling",
                "Pornography",
                "Firearms"
            };

            return forbidden.Contains(sectorName);
        }

        // =============================================
        // MAP SYMBOL TO PROJECT NEO SECTOR
        // =============================================
        private string MapSector(string symbol)
        {
            symbol = symbol.ToUpperInvariant();

            return symbol switch
            {
                // Technology
                "TCS.NS" => "Technology",
                "INFY.NS" => "Technology",
                "WIPRO.NS" => "Technology",
                "HCLTECH.NS" => "Technology",
                "TECHM.NS" => "Technology",
                "LTIM.NS" => "Technology",
                "MPHASIS.NS" => "Technology",
                "PERSISTENT.NS" => "Technology",
                "COFORGE.NS" => "Technology",
                "KPITTECH.NS" => "Technology",

                // Consumer Discretionary
                "TITAN.NS" => "Consumer Discretionary",
                "MARUTI.NS" => "Consumer Discretionary",
                "TATAMOTORS.NS" => "Consumer Discretionary",
                "BAJAJ-AUTO.NS" => "Consumer Discretionary",
                "HEROMOTOCO.NS" => "Consumer Discretionary",
                "EICHERMOT.NS" => "Consumer Discretionary",
                "M&M.NS" => "Consumer Discretionary",
                "TVSMOTOR.NS" => "Consumer Discretionary",
                "ASHOKLEY.NS" => "Consumer Discretionary",
                "BATAINDIA.NS" => "Consumer Discretionary",
                "PAGEIND.NS" => "Consumer Discretionary",
                "VOLTAS.NS" => "Consumer Discretionary",

                // Utilities
                "NTPC.NS" => "Utilities",
                "POWERGRID.NS" => "Utilities",
                "TATAPOWER.NS" => "Utilities",
                "ADANIGREEN.NS" => "Utilities",
                "ADANIPOWER.NS" => "Utilities",
                "TORNTPOWER.NS" => "Utilities",
                "CESC.NS" => "Utilities",
                "NHPC.NS" => "Utilities",
                "SJVN.NS" => "Utilities",

                // Communication Services
                "BHARTIARTL.NS" => "Communication Services",
                "ZOMATO.NS" => "Communication Services",
                "INDIAMART.NS" => "Communication Services",
                "NAUKRI.NS" => "Communication Services",
                "POLICYBZR.NS" => "Communication Services",
                "DELHIVERY.NS" => "Communication Services",
                "IRCTC.NS" => "Communication Services",
                "IDEA.NS" => "Communication Services",
                "JUSTDIAL.NS" => "Communication Services",

                // Energy
                "RELIANCE.NS" => "Energy",
                "ONGC.NS" => "Energy",
                "IOC.NS" => "Energy",
                "BPCL.NS" => "Energy",
                "GAIL.NS" => "Energy",
                "HINDPETRO.NS" => "Energy",
                "OIL.NS" => "Energy",
                "PETRONET.NS" => "Energy",

                // Consumer Staples
                "ITC.NS" => "Consumer Staples",
                "HINDUNILVR.NS" => "Consumer Staples",
                "NESTLEIND.NS" => "Consumer Staples",
                "BRITANNIA.NS" => "Consumer Staples",
                "DABUR.NS" => "Consumer Staples",
                "MARICO.NS" => "Consumer Staples",
                "GODREJCP.NS" => "Consumer Staples",
                "TATACONSUM.NS" => "Consumer Staples",
                "VBL.NS" => "Consumer Staples",
                "COLPAL.NS" => "Consumer Staples",

                // Pharma / Health Care
                "SUNPHARMA.NS" => "Pharma",
                "DRREDDY.NS" => "Pharma",
                "CIPLA.NS" => "Pharma",
                "DIVISLAB.NS" => "Pharma",
                "APOLLOHOSP.NS" => "Health Care",
                "LUPIN.NS" => "Pharma",
                "AUROPHARMA.NS" => "Pharma",
                "BIOCON.NS" => "Pharma",
                "ALKEM.NS" => "Pharma",
                "TORNTPHARM.NS" => "Pharma",

                // Industrials
                "LT.NS" => "Industrials",
                "SIEMENS.NS" => "Industrials",
                "ABB.NS" => "Industrials",
                "BHEL.NS" => "Industrials",
                "HAL.NS" => "Industrials",
                "CUMMINSIND.NS" => "Industrials",
                "THERMAX.NS" => "Industrials",
                "BEL.NS" => "Industrials",

                // Materials
                "JSWSTEEL.NS" => "Materials",
                "TATASTEEL.NS" => "Materials",
                "HINDALCO.NS" => "Materials",
                "VEDL.NS" => "Materials",
                "SAIL.NS" => "Materials",
                "COALINDIA.NS" => "Materials",
                "NMDC.NS" => "Materials",
                "NATIONALUM.NS" => "Materials",
                "GRASIM.NS" => "Materials",
                "ULTRACEMCO.NS" => "Materials",

                _ => "Unknown"
            };
        }

        // =============================================
        // SAFE DECIMAL CONVERSION
        // =============================================
        private decimal ToDecimal(dynamic? value)
        {
            try
            {
                if (value == null)
                    return 0m;

                return Convert.ToDecimal(value);
            }
            catch
            {
                return 0m;
            }
        }

        public async Task<List<Stock>> GetStocksFromBhavcopyAsync(int maxCount = 500)
        {
            Console.WriteLine("   → Loading NSE Bhavcopy CSV...");

            var possiblePaths = new List<string>
    {
        Path.Combine(AppContext.BaseDirectory, "nse_bhavcopy.csv"),
        Path.Combine(Directory.GetCurrentDirectory(), "nse_bhavcopy.csv")
    };

            var home = Environment.GetEnvironmentVariable("HOME");

            if (!string.IsNullOrWhiteSpace(home))
            {
                possiblePaths.Add(Path.Combine(home, "site", "wwwroot", "nse_bhavcopy.csv"));
            }

            string? filePath = null;

            foreach (var path in possiblePaths)
            {
                Console.WriteLine($"   → Checking Bhavcopy path: {path}");

                if (File.Exists(path))
                {
                    filePath = path;
                    Console.WriteLine($"   ✅ Bhavcopy file found: {path}");
                    break;
                }
            }

            if (filePath == null)
                throw new Exception("nse_bhavcopy.csv was not found in Function App package.");

            var csv = await File.ReadAllTextAsync(filePath);

            var lines = csv
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToList();

            if (lines.Count <= 1)
                throw new Exception("Bhavcopy CSV has no data rows.");

            var headers = SplitCsvLine(lines[0])
                .Select((h, i) => new { Name = h.Trim().ToUpperInvariant(), Index = i })
                .ToDictionary(x => x.Name, x => x.Index);

            int GetIndex(params string[] names)
            {
                foreach (var name in names)
                {
                    var key = name.ToUpperInvariant();

                    if (headers.ContainsKey(key))
                        return headers[key];
                }

                return -1;
            }

            var symbolIndex = GetIndex("SYMBOL", "TCKRSYMB");
            var seriesIndex = GetIndex("SERIES", "SCTYSRS");
            var nameIndex = GetIndex("SECURITY", "SECURITY NAME", "CMPNY_NM", "SECURITY_NM");
            var closeIndex = GetIndex("CLOSE", "CLOSE_PRICE", "CLS_PRIC", "CLOSE_PRICE");
            var prevCloseIndex = GetIndex("PREVCLOSE", "PREV_CLOSE", "PREV_CL_PR", "PRVS_CLSG_PRIC");
            var volumeIndex = GetIndex("TOTTRDQTY", "TTL_TRD_QNTY", "VOLUME", "TTL_TRD_QTY");
            var turnoverIndex = GetIndex("TOTTRDVAL", "TURNOVER", "TTL_TRD_VAL");

            if (symbolIndex < 0)
                throw new Exception("Bhavcopy CSV does not contain SYMBOL column.");

            if (closeIndex < 0)
                throw new Exception("Bhavcopy CSV does not contain CLOSE column.");

            var stocks = new List<Stock>();

            foreach (var line in lines.Skip(1))
            {
                var cols = SplitCsvLine(line);

                if (cols.Count <= symbolIndex)
                    continue;

                var symbol = cols[symbolIndex].Trim();

                if (string.IsNullOrWhiteSpace(symbol))
                    continue;

                var series = seriesIndex >= 0 && cols.Count > seriesIndex
                    ? cols[seriesIndex].Trim()
                    : "EQ";

                if (!series.Equals("EQ", StringComparison.OrdinalIgnoreCase))
                    continue;

                var stockName = nameIndex >= 0 && cols.Count > nameIndex
                    ? cols[nameIndex].Trim()
                    : symbol;

                var currentPrice = closeIndex >= 0 && cols.Count > closeIndex
                    ? ToDecimal(cols[closeIndex])
                    : 0m;

                var previousClose = prevCloseIndex >= 0 && cols.Count > prevCloseIndex
                    ? ToDecimal(cols[prevCloseIndex])
                    : 0m;

                if (currentPrice <= 0)
                    continue;

                if (previousClose <= 0)
                    previousClose = currentPrice;

                var pct1d = previousClose > 0
                    ? Math.Round(((currentPrice - previousClose) / previousClose) * 100m, 4)
                    : 0m;

                var volume = volumeIndex >= 0 && cols.Count > volumeIndex
                    ? ToDecimal(cols[volumeIndex])
                    : 0m;

                var turnover = turnoverIndex >= 0 && cols.Count > turnoverIndex
                    ? ToDecimal(cols[turnoverIndex])
                    : volume * currentPrice;

                var sector = MapSectorFromText(symbol, stockName);

                if (IsForbiddenSector(sector))
                    continue;

                stocks.Add(new Stock
                {
                    Symbol = symbol,
                    StockName = stockName,
                    SectorName = sector,
                    Price = currentPrice,
                    PreviousClose = previousClose,
                    Pct1d = pct1d,
                    Pct5d = 0m,
                    Pct20d = 0m,
                    MarketCap = 0m,
                    PERatio = 0m,
                    AvgTurnover30d = turnover
                });
            }

            stocks = stocks
                .OrderByDescending(s => s.AvgTurnover30d)
                .Take(maxCount)
                .ToList();

            Console.WriteLine($"   ✅ Bhavcopy stocks loaded: {stocks.Count}");

            return stocks;
        }

        private decimal ToDecimal(string? value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value))
                    return 0m;

                value = value.Replace(",", "").Trim();

                return Convert.ToDecimal(value);
            }
            catch
            {
                return 0m;
            }
        }

        // =============================================
        // EXPANDED FALLBACK SEED
        // Used only when Yahoo completely fails
        // =============================================
        private List<Stock> GetFallbackSeedStocks()
        {
            return new List<Stock>
            {
                // Technology
                new() { Symbol="TCS", StockName="TCS", SectorName="Technology", PreviousClose=3985m, Price=3985m, Pct1d=2.80m, Pct5d=5.20m, PERatio=28.4m },
                new() { Symbol="INFY", StockName="Infosys", SectorName="Technology", PreviousClose=1685m, Price=1685m, Pct1d=2.20m, Pct5d=4.10m, PERatio=24.6m },
                new() { Symbol="WIPRO", StockName="Wipro", SectorName="Technology", PreviousClose=485m, Price=485m, Pct1d=1.80m, Pct5d=3.40m, PERatio=22.8m },
                new() { Symbol="HCLTECH", StockName="HCL Tech", SectorName="Technology", PreviousClose=1580m, Price=1580m, Pct1d=1.50m, Pct5d=2.80m, PERatio=26.4m },
                new() { Symbol="TECHM", StockName="Tech Mahindra", SectorName="Technology", PreviousClose=1485m, Price=1485m, Pct1d=1.20m, Pct5d=2.20m, PERatio=20.4m },

                // Consumer Discretionary
                new() { Symbol="TITAN", StockName="Titan Company", SectorName="Consumer Discretionary", PreviousClose=3520m, Price=3520m, Pct1d=3.20m, Pct5d=5.80m, PERatio=44.2m },
                new() { Symbol="MARUTI", StockName="Maruti Suzuki", SectorName="Consumer Discretionary", PreviousClose=12450m, Price=12450m, Pct1d=2.50m, Pct5d=4.60m, PERatio=24.8m },
                new() { Symbol="TATAMOTORS", StockName="Tata Motors", SectorName="Consumer Discretionary", PreviousClose=965m, Price=965m, Pct1d=2.10m, Pct5d=3.90m, PERatio=10.4m },
                new() { Symbol="BAJAJ-AUTO", StockName="Bajaj Auto", SectorName="Consumer Discretionary", PreviousClose=8840m, Price=8840m, Pct1d=1.80m, Pct5d=3.30m, PERatio=22.6m },
                new() { Symbol="HEROMOTOCO", StockName="Hero MotoCorp", SectorName="Consumer Discretionary", PreviousClose=4320m, Price=4320m, Pct1d=1.50m, Pct5d=2.80m, PERatio=18.4m },

                // Utilities
                new() { Symbol="NTPC", StockName="NTPC", SectorName="Utilities", PreviousClose=368m, Price=368m, Pct1d=1.80m, Pct5d=3.30m, PERatio=14.8m },
                new() { Symbol="POWERGRID", StockName="Power Grid", SectorName="Utilities", PreviousClose=325m, Price=325m, Pct1d=1.40m, Pct5d=2.60m, PERatio=16.4m },
                new() { Symbol="TATAPOWER", StockName="Tata Power", SectorName="Utilities", PreviousClose=425m, Price=425m, Pct1d=1.10m, Pct5d=2.00m, PERatio=28.6m },
                new() { Symbol="ADANIGREEN", StockName="Adani Green Energy", SectorName="Utilities", PreviousClose=1685m, Price=1685m, Pct1d=0.80m, Pct5d=1.50m, PERatio=0m },
                new() { Symbol="ADANIPOWER", StockName="Adani Power", SectorName="Utilities", PreviousClose=585m, Price=585m, Pct1d=0.60m, Pct5d=1.10m, PERatio=0m },

                // Communication Services
                new() { Symbol="BHARTIARTL", StockName="Bharti Airtel", SectorName="Communication Services", PreviousClose=1685m, Price=1685m, Pct1d=2.40m, Pct5d=4.50m, PERatio=44.8m },
                new() { Symbol="ZOMATO", StockName="Zomato", SectorName="Communication Services", PreviousClose=245m, Price=245m, Pct1d=1.80m, Pct5d=3.30m, PERatio=0m },
                new() { Symbol="INDIAMART", StockName="IndiaMart", SectorName="Communication Services", PreviousClose=2485m, Price=2485m, Pct1d=1.40m, Pct5d=2.60m, PERatio=42.6m },
                new() { Symbol="NAUKRI", StockName="Info Edge", SectorName="Communication Services", PreviousClose=7285m, Price=7285m, Pct1d=1.10m, Pct5d=2.00m, PERatio=68.4m },

                // Energy
                new() { Symbol="RELIANCE", StockName="Reliance Industries", SectorName="Energy", PreviousClose=2985m, Price=2985m, Pct1d=2.50m, Pct5d=4.80m, PERatio=24.6m },
                new() { Symbol="ONGC", StockName="ONGC", SectorName="Energy", PreviousClose=265m, Price=265m, Pct1d=2.10m, Pct5d=3.90m, PERatio=8.4m },
                new() { Symbol="IOC", StockName="Indian Oil", SectorName="Energy", PreviousClose=168m, Price=168m, Pct1d=1.80m, Pct5d=3.30m, PERatio=6.8m },
                new() { Symbol="BPCL", StockName="BPCL", SectorName="Energy", PreviousClose=325m, Price=325m, Pct1d=1.50m, Pct5d=2.80m, PERatio=7.2m },

                // Consumer Staples
                new() { Symbol="ITC", StockName="ITC", SectorName="Consumer Staples", PreviousClose=465m, Price=465m, Pct1d=0.30m, Pct5d=0.60m, PERatio=22.8m },
                new() { Symbol="HINDUNILVR", StockName="HUL", SectorName="Consumer Staples", PreviousClose=2485m, Price=2485m, Pct1d=1.80m, Pct5d=3.40m, PERatio=58.4m },
                new() { Symbol="NESTLEIND", StockName="Nestle India", SectorName="Consumer Staples", PreviousClose=2285m, Price=2285m, Pct1d=1.50m, Pct5d=2.80m, PERatio=64.2m },
                new() { Symbol="DABUR", StockName="Dabur India", SectorName="Consumer Staples", PreviousClose=585m, Price=585m, Pct1d=1.20m, Pct5d=2.20m, PERatio=42.6m },

                // Pharma
                new() { Symbol="SUNPHARMA", StockName="Sun Pharma", SectorName="Pharma", PreviousClose=1680m, Price=1680m, Pct1d=2.80m, Pct5d=5.20m, PERatio=28.4m },
                new() { Symbol="DRREDDY", StockName="Dr Reddy's", SectorName="Pharma", PreviousClose=1285m, Price=1285m, Pct1d=2.20m, Pct5d=4.10m, PERatio=22.6m },
                new() { Symbol="CIPLA", StockName="Cipla", SectorName="Pharma", PreviousClose=1520m, Price=1520m, Pct1d=1.80m, Pct5d=3.40m, PERatio=24.8m },

                // Industrials
                new() { Symbol="LT", StockName="L&T", SectorName="Industrials", PreviousClose=3580m, Price=3580m, Pct1d=2.10m, Pct5d=3.90m, PERatio=28.4m },
                new() { Symbol="SIEMENS", StockName="Siemens India", SectorName="Industrials", PreviousClose=6840m, Price=6840m, Pct1d=1.85m, Pct5d=3.50m, PERatio=44.6m },
                new() { Symbol="ABB", StockName="ABB India", SectorName="Industrials", PreviousClose=8120m, Price=8120m, Pct1d=1.60m, Pct5d=3.00m, PERatio=42.8m },

                // Materials
                new() { Symbol="JSWSTEEL", StockName="JSW Steel", SectorName="Materials", PreviousClose=885m, Price=885m, Pct1d=2.20m, Pct5d=4.10m, PERatio=14.8m },
                new() { Symbol="TATASTEEL", StockName="Tata Steel", SectorName="Materials", PreviousClose=165m, Price=165m, Pct1d=1.80m, Pct5d=3.30m, PERatio=10.4m },
                new() { Symbol="HINDALCO", StockName="Hindalco", SectorName="Materials", PreviousClose=685m, Price=685m, Pct1d=1.50m, Pct5d=2.80m, PERatio=12.6m }
            };
        }

        // =============================================
        // OPTIONAL SAVE HELPER
        // =============================================
        public async Task SaveStocksToDatabase(List<Stock> stocks)
        {
            string runId = Guid.NewGuid().ToString();
            DateTime businessDate = DateTime.UtcNow.Date;

            await _db.InsertAllStocksAsync(runId, businessDate, stocks);
        }

        private class NseSecurity
        {
            public string Symbol { get; set; } = "";
            public string Series { get; set; } = "";
            public string SecurityName { get; set; } = "";
        }
    }
}