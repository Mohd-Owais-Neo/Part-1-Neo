using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NEO.Core.Data;
using NEO.Core.Models;

namespace NEO.Web.Pages
{
    public class HistoryModel : PageModel
    {
        private readonly IConfiguration _config;

        public List<RunSummary> Runs { get; set; } = new();
        public List<TradeSignal> Signals { get; set; } = new();
        public DateTime SelectedDate { get; set; } = DateTime.Today;

        public HistoryModel(IConfiguration config)
        {
            _config = config;
        }

        public async Task OnGetAsync(string? date)
        {
            var connStr = _config.GetConnectionString("DefaultConnection")!;
            var db = new DatabaseHelper(connStr);

            // Load run history
            var logs = await db.LoadRunHistoryAsync(30);

            // Group into run summaries
            Runs = logs
                .GroupBy(l => l.RunId)
                .Select(g =>
                {
                    var pipeline = g.FirstOrDefault(
                                       l => l.Stage == "PIPELINE");
                    var stage8 = g.FirstOrDefault(
                                       l => l.Stage == "STAGE_8");
                    var stage9 = g.FirstOrDefault(
                                       l => l.Stage == "STAGE_9");

                    return new RunSummary
                    {
                        RunId = g.Key,
                        BusinessDate = g.First().BusinessDate,
                        Status = pipeline?.Status ?? "",
                        PipelineMsg = pipeline?.Message ?? "",
                        SignalsMsg = stage8?.Message ?? "",
                        EmailMsg = stage9?.Message ?? "",
                        CreatedAt = g.Max(l => l.CreatedAt)
                    };
                })
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            // Load signals for selected date
            if (date != null && DateTime.TryParse(date, out var parsed))
                SelectedDate = parsed;
            else if (Runs.Count > 0)
                SelectedDate = Runs[0].BusinessDate;

            Signals = await db.LoadSignalsByDateAsync(SelectedDate);
        }
    }

    // ── View Model ──
    public class RunSummary
    {
        public string RunId { get; set; } = "";
        public DateTime BusinessDate { get; set; }
        public string Status { get; set; } = "";
        public string PipelineMsg { get; set; } = "";
        public string SignalsMsg { get; set; } = "";
        public string EmailMsg { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}

