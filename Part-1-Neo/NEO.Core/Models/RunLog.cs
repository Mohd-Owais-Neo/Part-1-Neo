using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Models
{
    public class RunLog
    {
        public int Id { get; set; }
        public string RunId { get; set; } = string.Empty;
        public DateTime BusinessDate { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
