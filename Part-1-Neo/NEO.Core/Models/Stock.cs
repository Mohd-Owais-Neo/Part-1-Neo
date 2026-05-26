using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Models
{
    public class Stock
    {
        public int Id { get; set; }
        public string RunId { get; set; } = string.Empty;
        public DateTime BusinessDate { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string StockName { get; set; } = string.Empty;
        public string SectorName { get; set; } = string.Empty;
        public decimal PreviousClose { get; set; }
        public decimal Pct1d { get; set; }
        public decimal Pct5d { get; set; }
        public decimal Pct20d { get; set; }
        public decimal MarketCap { get; set; }
        public decimal PeRatio { get; set; }
        public decimal AvgTurnover30d { get; set; }
        public int RankWithinSector { get; set; }
        public string ReasonCode { get; set; } = string.Empty;
        public decimal StopLoss { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal Score { get; set; }
        public int Rank { get; set; }
        public decimal PERatio { get; set; }
        public decimal Volume { get; set; }

    }
}
