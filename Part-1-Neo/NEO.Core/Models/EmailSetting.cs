using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Models
{
    public class EmailSettings
    {
        public string SmtpHost { get; set; } = "smtp.gmail.com";
        public int SmtpPort { get; set; } = 587;
        public string FromEmail { get; set; } = string.Empty;
        public string FromPassword { get; set; } = string.Empty;
        public string ToEmail { get; set; } = string.Empty;
    }
}
