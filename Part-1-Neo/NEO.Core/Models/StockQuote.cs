using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NEO.Core.Models
{
    public class StockQuote
    {
        public string Symbol { get; set; } = "";
        public decimal Price { get; set; }
        public decimal PrevClose { get; set; }
        public decimal Pct1d { get; set; }
    }
}