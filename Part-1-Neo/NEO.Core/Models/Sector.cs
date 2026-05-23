using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Models
{
    public class Sector
    {
        public int Id { get; set; }
        public string RunId { get; set; } = string.Empty;
        public DateTime BusinessDate { get; set; }
        public string SectorName { get; set; } = string.Empty;
        public decimal PctChange { get; set; }
        public decimal Pct1d { get; set; }
        public decimal Pct5d { get; set; }
        public decimal Pct20d { get; set; }
        public decimal Pct1dIndia { get; set; }
        public decimal Pct1dUS { get; set; }
        public decimal Pct1dChina { get; set; }
        public int Rank { get; set; }
        public string IntersectionGroup { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsTop3 { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}