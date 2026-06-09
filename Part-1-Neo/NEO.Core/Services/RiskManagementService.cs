using NEO.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Services
{
    public class RiskManagementService
    {
        // =============================================
        // RISK RULES SETTINGS
        // =============================================
        private const decimal StrongBuyMinScore = 2.0m;   // Score ≥ 2.0  → BUY
        private const decimal WatchMinScore = 0.5m;   // Score ≥ 0.5  → WATCH
        private const decimal MinMomentum = -1.0m;  // 1D% must be > -1%
        private const decimal StrongMomentum = 1.0m;   // 1D% ≥ 1% = strong
        private const decimal MaxPERatio = 45m;    // PE must be < 45
        private const decimal StopLossPct = 0.95m;  // stop_loss = close × 0.95
        private const int TopPerSector = 3;      // ← TOP 3 per sector

        // =============================================
        // APPLY RISK RULES — Pick TOP 3 per sector
        //                     + compute stop loss
        // =============================================
        // =============================================
        // APPLY RISK RULES — TOP 3 per sector
        // BUY first, fill with WATCH if < 3
        // =============================================
        public List<TradeSignal> ApplyRiskRules(List<Stock> stocks)
        {
            Console.WriteLine("\n🔵 STAGE 8 — Risk Management + Stop Loss...");
            Console.WriteLine($"   → Evaluating {stocks.Count} stocks");

            // Step 1 — Evaluate every stock
            var allSignals = stocks.Select(s => EvaluateStock(s)).ToList();

            // Step 2 — For each sector, pick top 3
            //          BUY first → fill remainder with WATCH
            var finalSignals = new List<TradeSignal>();

            var sectors = allSignals
                .Select(s => s.SectorName)
                .Distinct()
                .ToList();

            foreach (var sector in sectors)
            {
                var sectorAll = allSignals
                    .Where(s => s.SectorName == sector)
                    .ToList();

                // Top BUY stocks for this sector
                var buys = sectorAll
                    .Where(s => s.Signal == "BUY")
                    .OrderByDescending(s => s.Pct1d)
                    .ThenByDescending(s => s.Pct5d)
                    .ThenByDescending(s => s.Score)
                    .Take(TopPerSector)
                    .ToList();

                finalSignals.AddRange(buys);

                // Fill remaining slots with WATCH if BUY count < 3
                int remaining = TopPerSector - buys.Count;
                if (remaining > 0)
                {
                    var usedSymbols = buys.Select(b => b.Symbol).ToHashSet();

                    var watchFills = sectorAll
                        .Where(s => s.Signal == "WATCH"
                                 && !usedSymbols.Contains(s.Symbol))
                        .OrderByDescending(s => s.Pct1d)
                        .ThenByDescending(s => s.Score)
                        .Take(remaining)
                        .ToList();

                    if (watchFills.Count > 0)
                        Console.WriteLine($"   ⚠️ {sector}: only {buys.Count} BUY " +
                                          $"→ filling {watchFills.Count} from WATCH");

                    finalSignals.AddRange(watchFills);
                }
            }

            // Step 3 — Final sort and re-rank
            finalSignals = finalSignals
                .OrderByDescending(s => s.Pct1d)
                .ThenByDescending(s => s.Score)
                .ToList();

            for (int i = 0; i < finalSignals.Count; i++)
                finalSignals[i].Rank = i + 1;

            // Step 4 — Print final table
            Console.WriteLine("\n   📊 Final Picks (Top 3 per Sector):");
            Console.WriteLine($"   {"#",-3} {"Symbol",-8} {"Sector",-25} " +
                              $"{"1D%",7}  {"Close",8}  {"StopLoss",10}  Signal");
            Console.WriteLine($"   {new string('-', 85)}");

            foreach (var s in finalSignals)
                Console.WriteLine($"   {s.Rank,-3} " +
                                  $"{s.Symbol,-8} " +
                                  $"{s.SectorName,-25} " +
                                  $"{s.Pct1d,6:F2}%  " +
                                  $"{s.PreviousClose,8:F2}  " +
                                  $"{s.StopLoss,10:F2}  " +
                                  $"{s.Signal}");

            Console.WriteLine($"   {new string('-', 85)}");

            foreach (var grp in finalSignals.GroupBy(s => s.SectorName))
                Console.WriteLine($"   📂 {grp.Key}: {grp.Count()} stocks");

            Console.WriteLine($"\n   ✅ Total final picks: {finalSignals.Count}");
            Console.WriteLine("\n✅ STAGE 8 COMPLETE");
            return finalSignals;
        }


        // =============================================
        // EVALUATE SINGLE STOCK
        // =============================================
        private TradeSignal EvaluateStock(Stock stock)
        {
            var reasons = new List<string>();
            bool hardFail = false;

            // Hard fail rules → SKIP immediately
            if (stock.Pct1d < MinMomentum)
            {
                hardFail = true;
                reasons.Add($"momentum too low ({stock.Pct1d:F2}%)");
            }

            if (stock.PERatio > MaxPERatio && stock.PERatio > 0)
            {
                hardFail = true;
                reasons.Add($"PE too high ({stock.PERatio:F1})");
            }

            // Compute stop loss: previous_close × 0.95
            var previousClose = stock.PreviousClose > 0
                ? stock.PreviousClose
                : stock.Price;  // fallback to Price if PreviousClose is missing

            var stopLoss = Math.Round(previousClose * StopLossPct, 2);

            if (hardFail)
                return new TradeSignal
                {
                    Symbol = stock.Symbol,
                    StockName = stock.StockName,
                    SectorName = stock.SectorName,
                    Rank = stock.Rank,
                    Score = stock.Score,
                    Pct1d = stock.Pct1d,
                    Pct5d = stock.Pct5d,
                    PreviousClose = previousClose,
                    StopLoss = stopLoss,
                    Signal = "SKIP",
                    Reason = string.Join(", ", reasons)
                };

            // Determine signal strength
            string signal;
            string reason;

            if (stock.Score >= StrongBuyMinScore && stock.Pct1d >= StrongMomentum)
            {
                signal = "BUY";
                reason = $"strong score ({stock.Score:F3}) + " +
                         $"momentum ({stock.Pct1d:F2}%)";
            }
            else if (stock.Score >= WatchMinScore)
            {
                signal = "WATCH";
                reason = $"score OK ({stock.Score:F3}) but " +
                         $"momentum weak ({stock.Pct1d:F2}%)";
            }
            else
            {
                signal = "SKIP";
                reason = $"score too low ({stock.Score:F3})";
            }

            return new TradeSignal
            {
                Symbol = stock.Symbol,
                StockName = stock.StockName,
                SectorName = stock.SectorName,
                Rank = stock.Rank,
                Score = stock.Score,
                Pct1d = stock.Pct1d,
                Pct5d = stock.Pct5d,
                PreviousClose = previousClose,
                StopLoss = stopLoss,
                Signal = signal,
                Reason = reason
            };
        }
    }
}
