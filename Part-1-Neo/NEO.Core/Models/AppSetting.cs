using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Models
{
    public class AppSettings
    {
        public string AlphaVantageApiKey { get; set; } = string.Empty;
        public string EmailFrom { get; set; } = string.Empty;
        public string EmailAppPassword { get; set; } = string.Empty;
        public string EmailTo { get; set; } = string.Empty;
        public int TopStocksPerSector { get; set; } = 10;
        public decimal MinTurnoverThreshold { get; set; } = 1000000;
    }
}