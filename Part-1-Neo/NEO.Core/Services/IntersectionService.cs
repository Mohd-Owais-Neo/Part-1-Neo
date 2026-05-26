using NEO.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Services
{
    public class IntersectionService
    {
        // =============================================
        // MAIN METHOD — Find Intersecting Sectors
        // =============================================
        public List<string> FindIntersectingSectors(
            List<Sector> usSectors,
            List<Sector> indiaSectors,
            List<Sector> chinaSectors,
            int topN = 5)
        {
            Console.WriteLine("\n🔵 INTERSECTION LOGIC — Finding strong sectors...");

            // Step 1 — Get top N sector names from each market
            var usTop = GetTopSectorNames(usSectors, topN);
            var indiaTop = GetTopSectorNames(indiaSectors, topN);
            var chinaTop = GetTopSectorNames(chinaSectors, topN);

            Console.WriteLine($"\n   📊 Top {topN} US Sectors:");
            usTop.ForEach(s => Console.WriteLine($"      → {s}"));

            Console.WriteLine($"\n   📊 Top {topN} India Sectors:");
            indiaTop.ForEach(s => Console.WriteLine($"      → {s}"));

            Console.WriteLine($"\n   📊 Top {topN} China Sectors:");
            chinaTop.ForEach(s => Console.WriteLine($"      → {s}"));

            // Step 2 — Find sectors in ALL 3 markets
            var intersection = FindCommonSectors(usTop, indiaTop, chinaTop);

            Console.WriteLine($"\n   🎯 Sectors in ALL 3 markets: {intersection.Count}");
            intersection.ForEach(s => Console.WriteLine($"      ✅ {s}"));

            // Step 3 — If no full intersection, try US + India only
            if (intersection.Count == 0)
            {
                Console.WriteLine("   ⚠️ No 3-way intersection — trying US + India...");
                intersection = usTop
                    .Intersect(indiaTop, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                Console.WriteLine($"   → US + India intersection: {intersection.Count}");
                intersection.ForEach(s => Console.WriteLine($"      ✅ {s}"));
            }

            // Step 4 — If still empty, fall back to top US sectors
            if (intersection.Count == 0)
            {
                Console.WriteLine("   ⚠️ No intersection found — using top 3 US sectors");
                intersection = usTop.Take(3).ToList();
                intersection.ForEach(s => Console.WriteLine($"      ✅ {s} (fallback)"));
            }

            Console.WriteLine($"\n   ✅ Final selected sectors: {intersection.Count}");
            return intersection;
        }

        // =============================================
        // SCORE SECTORS — Rank by combined performance
        // =============================================
        public List<SectorScore> ScoreSectors(
            List<Sector> usSectors,
            List<Sector> indiaSectors,
            List<Sector> chinaSectors)
        {
            Console.WriteLine("\n   → Scoring sectors by combined performance...");

            var scores = new List<SectorScore>();

            // Normalize sector names for matching
            var usDict = usSectors.ToDictionary(
                s => s.SectorName.ToLower(), s => s);
            var indiaDict = indiaSectors.ToDictionary(
                s => s.SectorName.ToLower(), s => s);
            var chinaDict = chinaSectors.ToDictionary(
                s => s.SectorName.ToLower(), s => s);

            // Score each US sector
            foreach (var us in usSectors)
            {
                var key = us.SectorName.ToLower();

                // Find matching India sector
                var indiaMatch = indiaDict
                    .FirstOrDefault(x => IsSectorMatch(x.Key, key));
                var chinaMatch = chinaDict
                    .FirstOrDefault(x => IsSectorMatch(x.Key, key));

                var indiaScore = indiaMatch.Value?.PctChange ?? 0;
                var chinaScore = chinaMatch.Value?.PctChange ?? 0;

                // Combined score — weighted average
                // US: 40%, India: 35%, China: 25%
                var combined = (us.PctChange * 0.40m)
                             + (indiaScore * 0.35m)
                             + (chinaScore * 0.25m);

                scores.Add(new SectorScore
                {
                    SectorName = us.SectorName,
                    USPct = us.PctChange,
                    IndiaPct = indiaScore,
                    ChinaPct = chinaScore,
                    CombinedScore = combined
                });
            }

            // Sort by combined score
            scores = scores.OrderByDescending(s => s.CombinedScore).ToList();

            Console.WriteLine("\n   📊 Sector Scores (Combined):");
            foreach (var s in scores.Take(5))
                Console.WriteLine(
                    $"   {s.SectorName,-28} " +
                    $"US:{s.USPct,7:F2}% " +
                    $"IN:{s.IndiaPct,7:F2}% " +
                    $"CN:{s.ChinaPct,7:F2}% " +
                    $"→ Score:{s.CombinedScore,7:F2}%");

            return scores;
        }

        // =============================================
        // HELPERS
        // =============================================
        private List<string> GetTopSectorNames(List<Sector> sectors, int topN)
            => sectors
                .Where(s => s.PctChange > 0)   // Only positive sectors
                .OrderByDescending(s => s.PctChange)
                .Take(topN)
                .Select(s => s.SectorName)
                .ToList();

        private List<string> FindCommonSectors(
            List<string> us,
            List<string> india,
            List<string> china)
        {
            var result = new List<string>();

            foreach (var usSector in us)
            {
                var inIndiaAndChina =
                    india.Any(i => IsSectorMatch(i, usSector)) &&
                    china.Any(c => IsSectorMatch(c, usSector));

                if (inIndiaAndChina)
                    result.Add(usSector);
            }

            return result;
        }

        // Fuzzy sector name matching
        // "Technology" matches "Technology", "Tech", "IT"
        private bool IsSectorMatch(string a, string b)
        {
            a = a.ToLower().Trim();
            b = b.ToLower().Trim();

            if (a == b) return true;

            // Common aliases
            var aliases = new Dictionary<string, List<string>>
            {
                { "technology",   new List<string> { "tech", "it", "information technology" } },
                { "health care",  new List<string> { "healthcare", "pharma", "health" } },
                { "pharma",       new List<string> { "health care", "healthcare", "health" } },
                { "energy",       new List<string> { "oil", "gas", "power" } },
                { "industrials",  new List<string> { "industrial", "manufacturing" } },
                { "consumer",     new List<string> { "consumer staples", "consumer discretionary", "fmcg" } },
                { "fmcg",         new List<string> { "consumer staples", "consumer" } },
                { "materials",    new List<string> { "metals", "mining", "basic materials" } },
                { "metals",       new List<string> { "materials", "mining" } },
                { "auto",         new List<string> { "consumer discretionary", "automobile" } }
            };

            if (aliases.ContainsKey(a) && aliases[a].Contains(b)) return true;
            if (aliases.ContainsKey(b) && aliases[b].Contains(a)) return true;

            // Partial match
            if (a.Contains(b) || b.Contains(a)) return true;

            return false;
        }
    }

    // =============================================
    // SECTOR SCORE MODEL
    // =============================================
    public class SectorScore
    {
        public string SectorName { get; set; } = "";
        public decimal USPct { get; set; }
        public decimal IndiaPct { get; set; }
        public decimal ChinaPct { get; set; }
        public decimal CombinedScore { get; set; }
    }
}
