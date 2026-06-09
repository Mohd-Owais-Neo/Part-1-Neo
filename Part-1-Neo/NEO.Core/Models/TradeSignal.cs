using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Models
{
    public class TradeSignal
    {
        public string Symbol { get; set; } = "";
        public string StockName { get; set; } = "";
        public string SectorName { get; set; } = "";
        public int Rank { get; set; }
        public decimal Score { get; set; }
        public decimal Pct1d { get; set; }
        public decimal Pct5d { get; set; }
        public decimal PreviousClose { get; set; }   // ← NEW
        public decimal StopLoss { get; set; }         // ← NEW (PreviousClose × 0.95)
        public string Signal { get; set; } = "";      // BUY / WATCH / SKIP
        public string Reason { get; set; } = "";
        public DateTime BusinessDate { get; set; }
        public string RunId { get; set; } = "";
    }
}
