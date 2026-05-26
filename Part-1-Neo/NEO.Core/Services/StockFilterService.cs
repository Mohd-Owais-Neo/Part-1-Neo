using NEO.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Services
{
    public class StockFilterService
    {
        // =============================================
        // FILTER SETTINGS
        // =============================================
        private const decimal MinTurnover = 10_000_000m;  // $10M min daily turnover
        private const decimal MaxPERatio = 50m;          // Max PE ratio
        private const decimal MinPERatio = 1m;           // Min PE (avoid negatives)
        private const decimal MinMomentum = -2m;          // Min 1D % change

        // =============================================
        // MAIN FILTER METHOD
        // =============================================
        public List<Stock> FilterStocks(List<Stock> stocks, string sectorName)
        {
            Console.WriteLine($"\n   → Filtering {stocks.Count} stocks " +
                              $"for sector: {sectorName}");

            var passed = new List<Stock>();
            var failed = new List<Stock>();

            foreach (var stock in stocks)
            {
                var reasons = new List<string>();

                // Check momentum
                if (stock.Pct1d < MinMomentum)
                    reasons.Add($"momentum too low ({stock.Pct1d:F2}%)");

                // Check PE ratio (only if available)
                if (stock.PERatio > 0)
                {
                    if (stock.PERatio > MaxPERatio)
                        reasons.Add($"PE too high ({stock.PERatio:F1})");
                    if (stock.PERatio < MinPERatio)
                        reasons.Add($"PE invalid ({stock.PERatio:F1})");
                }

                // Check turnover (only if available)
                if (stock.AvgTurnover30d > 0 && stock.AvgTurnover30d < MinTurnover)
                    reasons.Add($"turnover too low (${stock.AvgTurnover30d:N0})");

                if (reasons.Count == 0)
                {
                    passed.Add(stock);
                    Console.WriteLine($"   ✅ {stock.Symbol,-8} " +
                                      $"1D:{stock.Pct1d,6:F2}% " +
                                      $"PE:{stock.PERatio,6:F1} " +
                                      $"→ PASSED");
                }
                else
                {
                    failed.Add(stock);
                    Console.WriteLine($"   ❌ {stock.Symbol,-8} " +
                                      $"→ FAILED ({string.Join(", ", reasons)})");
                }
            }

            Console.WriteLine($"\n   → {passed.Count} passed / " +
                              $"{failed.Count} failed");

            // If too few passed → relax filters and retry
            if (passed.Count < 3 && stocks.Count > 0)
            {
                Console.WriteLine("   ⚠️ Too few stocks passed — relaxing filters...");
                passed = RelaxedFilter(stocks);
            }

            return passed;
        }

        // =============================================
        // RELAXED FILTER — Used as fallback
        // =============================================
        private List<Stock> RelaxedFilter(List<Stock> stocks)
        {
            Console.WriteLine("   → Applying relaxed filter " +
                              "(momentum > -5%, PE < 100)...");

            var result = stocks
                .Where(s => s.Pct1d > -5m)
                .Where(s => s.PERatio < 100m || s.PERatio == 0)
                .OrderByDescending(s => s.Pct1d)
                .ToList();

            Console.WriteLine($"   → {result.Count} stocks after relaxed filter");
            return result;
        }

        // =============================================
        // SCORE AND RANK STOCKS
        // =============================================
        public List<Stock> ScoreAndRankStocks(List<Stock> stocks)
        {
            if (stocks.Count == 0) return stocks;

            Console.WriteLine("\n   → Scoring and ranking stocks...");

            foreach (var stock in stocks)
            {
                // Score = weighted combination of factors
                // Momentum  : 50%
                // PE inverse : 30% (lower PE = better)
                // Turnover  : 20%

                decimal momentumScore = stock.Pct1d * 0.50m;

                decimal peScore = 0;
                if (stock.PERatio > 0 && stock.PERatio <= MaxPERatio)
                    peScore = (1m - (stock.PERatio / MaxPERatio)) * 0.30m * 10m;

                decimal turnoverScore = 0;
                if (stock.AvgTurnover30d > 0)
                    turnoverScore = Math.Min(
                        (stock.AvgTurnover30d / 1_000_000_000m) * 0.20m, 0.20m);

                stock.Score = momentumScore + peScore + turnoverScore;
            }

            // Rank by score
            var ranked = stocks
                .OrderByDescending(s => s.Score)
                .ToList();

            for (int i = 0; i < ranked.Count; i++)
                ranked[i].Rank = i + 1;

            Console.WriteLine("\n   📊 Stock Rankings:");
            foreach (var s in ranked.Take(10))
                Console.WriteLine($"   Rank {s.Rank}: {s.Symbol,-8} " +
                                  $"1D:{s.Pct1d,6:F2}%  " +
                                  $"Score:{s.Score,6:F3}");

            return ranked;
        }
    }
}
