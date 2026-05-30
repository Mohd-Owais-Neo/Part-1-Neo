using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NEO.Core.Data;
using NEO.Core.Models;

namespace NEO.Web.Pages
{
    public class IndexModel : PageModel
    {
        private readonly IConfiguration _config;

        public string LastRunId { get; set; } = "";
        public DateTime LastRunDate { get; set; }
        public List<TradeSignal>
    Signals
        { get; set; } = new();
        public List<Sector>
            SectorsUS
        { get; set; } = new();
        public List<Sector>
            SectorsIndia
        { get; set; } = new();
        public List<Sector>
            SectorsChina
        { get; set; } = new();
        public List<string>
            SelectedSectors
        { get; set; } = new();

        public int BuyCount => Signals.Count(s => s.Signal == "BUY");
        public int WatchCount => Signals.Count(s => s.Signal == "WATCH");
        public int SkipCount => Signals.Count(s => s.Signal == "SKIP");

        public IndexModel(IConfiguration config)
        {
            _config = config;
        }

        public async Task OnGetAsync()
        {
            var connStr = _config.GetConnectionString("DefaultConnection")!;
            var db = new DatabaseHelper(connStr);

            // Load today's signals
            Signals = await db.LoadTodaySignalsAsync();
            SectorsUS = await db.LoadTodaySectorsAsync("US");
            SectorsIndia = await db.LoadTodaySectorsAsync("INDIA");
            SectorsChina = await db.LoadTodaySectorsAsync("CHINA");
            SelectedSectors = await db.LoadSelectedSectorsAsync();

            if (Signals.Count > 0)
            {
                LastRunDate = Signals[0].BusinessDate;
                LastRunId = Signals[0].RunId;
            }
        }
    }
}
