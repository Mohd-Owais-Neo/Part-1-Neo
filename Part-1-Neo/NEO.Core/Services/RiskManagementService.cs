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

        // =============================================
        // APPLY RISK RULES TO ALL STOCKS
        // =============================================
        public List<TradeSignal> ApplyRiskRules(List<Stock> stocks)
        {
            Console.WriteLine("\n🔵 STAGE 8 — Risk Management...");
            Console.WriteLine($"   → Applying risk rules to {stocks.Count} stocks");

            var signals = new List<TradeSignal>();

            foreach (var stock in stocks)
            {
                var signal = EvaluateStock(stock);
                signals.Add(signal);
            }

            // Print summary
            var buys = signals.Count(s => s.Signal == "BUY");
            var watches = signals.Count(s => s.Signal == "WATCH");
            var skips = signals.Count(s => s.Signal == "SKIP");

            Console.WriteLine("\n   📊 Risk Assessment Results:");
            Console.WriteLine($"   {"Symbol",-8} {"Signal",-6} {"Score",8}  {"1D%",7}  Reason");
            Console.WriteLine($"   {new string('-', 60)}");

            foreach (var s in signals.OrderBy(s => s.Rank))
                Console.WriteLine($"   {s.Symbol,-8} " +
                                  $"{s.Signal,-6} " +
                                  $"{s.Score,8:F3}  " +
                                  $"{s.Pct1d,6:F2}%  " +
                                  $"{s.Reason}");

            Console.WriteLine($"   {new string('-', 60)}");
            Console.WriteLine($"   🟢 BUY   : {buys}");
            Console.WriteLine($"   🟡 WATCH : {watches}");
            Console.WriteLine($"   🔴 SKIP  : {skips}");

            Console.WriteLine("\n✅ STAGE 8 COMPLETE");
            return signals;
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

            if (hardFail)
                return new TradeSignal
                {
                    Symbol = stock.Symbol,
                    StockName = stock.StockName,
                    SectorName = stock.SectorName,
                    Rank = stock.Rank,
                    Score = stock.Score,
                    Pct1d = stock.Pct1d,
                    Signal = "SKIP",
                    Reason = string.Join(", ", reasons)
                };

            // Determine signal strength
            string signal;
            string reason;

            if (stock.Score >= StrongBuyMinScore && stock.Pct1d >= StrongMomentum)
            {
                signal = "BUY";
                reason = $"strong score ({stock.Score:F3}) + momentum ({stock.Pct1d:F2}%)";
            }
            else if (stock.Score >= WatchMinScore)
            {
                signal = "WATCH";
                reason = $"score OK ({stock.Score:F3}) but momentum weak ({stock.Pct1d:F2}%)";
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
                Signal = signal,
                Reason = reason
            };
        }
    }
}

