using MailKit.Security;
using MimeKit;
using NEO.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Services
{
    public class EmailAlertService
    {
        private readonly string _smtpHost;
        private readonly int _smtpPort;
        private readonly string _fromEmail;
        private readonly string _fromPassword;
        private readonly string _toEmail;

        public EmailAlertService(
            string smtpHost,
            int smtpPort,
            string fromEmail,
            string fromPassword,
            string toEmail)
        {
            _smtpHost = smtpHost;
            _smtpPort = smtpPort;
            _fromEmail = fromEmail;
            _fromPassword = fromPassword;
            _toEmail = toEmail;
        }

        // =============================================
        // SEND DAILY TRADE SIGNAL EMAIL
        // =============================================
        public async Task SendDailySignalAsync(
            string runId,
            DateTime businessDate,
            List<string> selectedSectors,
            List<TradeSignal> signals)
        {
            Console.WriteLine("\n🔵 STAGE 9 — Email Alert...");
            Console.WriteLine($"   → Sending to: {_toEmail}");
            Console.WriteLine($"   → Sectors: {selectedSectors.Count} | Picks: {signals.Count}");

            try
            {
                var subject = $"[NEO] Daily Picks — {businessDate:yyyy-MM-dd} — {runId}";
                var body = BuildEmailBody(runId, businessDate, selectedSectors, signals);

                using var client = new SmtpClient(_smtpHost, _smtpPort)
                {
                    Credentials = new NetworkCredential(_fromEmail, _fromPassword),
                    EnableSsl = true
                };

                var mail = new MailMessage
                {
                    From = new MailAddress(_fromEmail, "ProjectNEO"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                mail.To.Add(_toEmail);

                await client.SendMailAsync(mail);

                Console.WriteLine("   ✅ Email sent successfully!");
                Console.WriteLine("✅ STAGE 9 COMPLETE");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Email failed: {ex.Message}");
                Console.WriteLine("   → Pipeline continues without email");
            }
        }

        // =============================================
        // BUILD HTML EMAIL BODY
        // 3 sector blocks — each with top 3 stocks
        // =============================================
        private string BuildEmailBody(
            string runId,
            DateTime businessDate,
            List<string> selectedSectors,
            List<TradeSignal> signals)
        {
            // Group signals by sector — top 3 stocks per sector
            var signalsBySector = signals
                .GroupBy(s => s.SectorName)
                .ToDictionary(g => g.Key, g => g
                    .OrderByDescending(s => s.Pct1d)
                    .ThenByDescending(s => s.Score)
                    .Take(3)
                    .ToList());

            // Build one table block per sector
            var sectorBlocks = new System.Text.StringBuilder();
            int sectorNum = 0;

            foreach (var sector in selectedSectors.Take(3))
            {
                sectorNum++;

                // Find stocks for this sector (fuzzy match)
                var sectorStocks = signalsBySector
                    .FirstOrDefault(kvp =>
                        kvp.Key.Equals(sector, StringComparison.OrdinalIgnoreCase)
                        || kvp.Key.Contains(sector, StringComparison.OrdinalIgnoreCase)
                        || sector.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    .Value ?? new List<TradeSignal>();

                // Sector colour based on overall performance
                var avgPct = sectorStocks.Any() ? sectorStocks.Average(s => s.Pct1d) : 0;
                var sectorColor = avgPct >= 0 ? "#27ae60" : "#e74c3c";
                var arrow = avgPct >= 0 ? "▲" : "▼";

                sectorBlocks.Append($@"
  <!-- SECTOR {sectorNum}: {sector} -->
  <div style='background:white; border-radius:10px; margin-bottom:24px;
              box-shadow:0 2px 6px rgba(0,0,0,0.08); overflow:hidden;'>

    <!-- Sector Header -->
    <div style='background:{sectorColor}; padding:12px 20px; color:white;'>
      <table width='100%' cellpadding='0' cellspacing='0'>
        <tr>
          <td>
            <span style='font-size:18px; font-weight:bold;'>
              #{sectorNum} — {sector}
            </span>
          </td>
          <td align='right'>
            <span style='font-size:15px;'>
              {arrow} Avg 1D: {avgPct:+0.00;-0.00}%
            </span>
          </td>
        </tr>
      </table>
    </div>

    <!-- Stocks Table -->
    {(sectorStocks.Any()
        ? BuildSectorTable(sectorStocks, sectorColor)
        : "<p style='padding:15px; color:#aaa; font-style:italic;'>No picks available for this sector today</p>")}

  </div>");
            }

            // Plain text summary for email clients that block HTML
            var plainSummary = string.Join("  |  ", signals.Select(s =>
                $"{s.Symbol}({s.Signal}) SL:{s.StopLoss:F2}"));

            return $@"
<!DOCTYPE html>
<html>
<body style='font-family:Arial,sans-serif; max-width:900px; margin:auto;
             background:#f0f2f5; padding:20px;'>

  <!-- HEADER -->
  <div style='background:#1a1a2e; color:white; padding:22px 25px;
              border-radius:10px; margin-bottom:20px;'>
    <h2 style='margin:0; font-size:22px; letter-spacing:0.5px;'>
      📈 ProjectNEO — Daily Trade Picks
    </h2>
    <p style='margin:8px 0 0 0; color:#aac; font-size:13px;'>
      Run ID: <strong>{runId}</strong>
      &nbsp;&nbsp;|&nbsp;&nbsp;
      Date: <strong>{businessDate:dddd, dd MMMM yyyy}</strong>
    </p>
  </div>

  <!-- SUMMARY BAR -->
  <div style='background:white; border-radius:10px; padding:16px 20px;
              margin-bottom:20px; display:flex; gap:0;
              box-shadow:0 2px 6px rgba(0,0,0,0.08);'>
    <div style='flex:1; text-align:center; border-right:1px solid #eee;'>
      <div style='font-size:26px; font-weight:bold; color:#1a1a2e;'>
        {selectedSectors.Take(3).Count()}
      </div>
      <div style='color:#888; font-size:12px; margin-top:3px;'>Sectors</div>
    </div>
    <div style='flex:1; text-align:center; border-right:1px solid #eee;'>
      <div style='font-size:26px; font-weight:bold; color:#27ae60;'>
        {signals.Count(s => s.Signal == "BUY")}
      </div>
      <div style='color:#888; font-size:12px; margin-top:3px;'>🟢 BUY</div>
    </div>
    <div style='flex:1; text-align:center; border-right:1px solid #eee;'>
      <div style='font-size:26px; font-weight:bold; color:#f39c12;'>
        {signals.Count(s => s.Signal == "WATCH")}
      </div>
      <div style='color:#888; font-size:12px; margin-top:3px;'>🟡 WATCH</div>
    </div>
    <div style='flex:1; text-align:center;'>
      <div style='font-size:26px; font-weight:bold; color:#3498db;'>
        {signals.Count}
      </div>
      <div style='color:#888; font-size:12px; margin-top:3px;'>Total Picks</div>
    </div>
  </div>

  <!-- 3 SECTOR BLOCKS -->
  {sectorBlocks}

  <!-- PLAIN TEXT FALLBACK -->
  <div style='background:#fff8e1; padding:12px 16px; border-radius:8px;
              border-left:4px solid #f39c12; margin-bottom:20px;
              font-size:12px; color:#666;'>
    <strong>Plain Text:</strong> {plainSummary}
  </div>

  <!-- FOOTER -->
  <div style='text-align:center; color:#aaa; font-size:12px; padding:10px;'>
    ProjectNEO &nbsp;•&nbsp; {DateTime.Now:dd MMM yyyy HH:mm}
    &nbsp;•&nbsp; Stop Loss = Previous Close × 95%
  </div>

</body>
</html>";
        }

        // =============================================
        // BUILD STOCK TABLE FOR ONE SECTOR
        // Columns: #, Symbol, Name, Prev Close,
        //          1D%, 5D%, Stop Loss, Signal
        // =============================================
        private string BuildSectorTable(
            List<TradeSignal> stocks,
            string accentColor)
        {
            var rows = string.Join("", stocks.Select((s, i) => $@"
              <tr style='background:{(i % 2 == 0 ? "#fafafa" : "white")};'>
                <td style='padding:10px 12px; font-weight:bold;
                           color:#888; font-size:13px;'>{i + 1}</td>
                <td style='padding:10px 12px; font-weight:bold;
                           color:#1a1a2e; font-size:15px;'>{s.Symbol}</td>
                <td style='padding:10px 12px; color:#444;'>{s.StockName}</td>
                <td style='padding:10px 12px; text-align:right; color:#555;'>
                  {(s.PreviousClose > 0 ? $"₹{s.PreviousClose:N2}" : "—")}
                </td>
                <td style='padding:10px 12px; text-align:right; font-weight:bold;
                           color:{(s.Pct1d >= 0 ? "#27ae60" : "#e74c3c")};'>
                  {s.Pct1d:+0.00;-0.00}%
                </td>
                <td style='padding:10px 12px; text-align:right;
                           color:{(s.Pct5d >= 0 ? "#27ae60" : "#e74c3c")};'>
                  {s.Pct5d:+0.00;-0.00}%
                </td>
                <td style='padding:10px 12px; text-align:right;
                           font-weight:bold; color:#e74c3c;'>
                  {(s.StopLoss > 0 ? $"${s.StopLoss:F2}" : "—")}
                </td>
                <td style='padding:10px 12px; text-align:center;'>
                  <span style='background:{accentColor}; color:white;
                               padding:3px 10px; border-radius:12px;
                               font-size:12px; font-weight:bold;'>
                    {s.Signal}
                  </span>
                </td>
              </tr>"));

            return $@"
            <table style='width:100%; border-collapse:collapse; font-size:14px;'>
              <thead>
                <tr style='background:#f5f6fa; color:#555;
                           font-size:12px; text-transform:uppercase;'>
                  <th style='padding:9px 12px; text-align:left;'>#</th>
                  <th style='padding:9px 12px; text-align:left;'>Symbol</th>
                  <th style='padding:9px 12px; text-align:left;'>Stock Name</th>
                  <th style='padding:9px 12px; text-align:right;'>Prev Close</th>
                  <th style='padding:9px 12px; text-align:right;'>1D %</th>
                  <th style='padding:9px 12px; text-align:right;'>5D %</th>
                  <th style='padding:9px 12px; text-align:right;'>Stop Loss</th>
                  <th style='padding:9px 12px; text-align:center;'>Signal</th>
                </tr>
              </thead>
              <tbody>
                {rows}
              </tbody>
            </table>";
        }
        public async Task SendStockFetchSummaryAsync(
            string runId,
            DateTime businessDate,
            List<Stock> stocks,
            string status,
            string message)
        {
            Console.WriteLine("\n🔵 STOCK FETCH EMAIL ALERT...");
            Console.WriteLine($"   → Status: {status}");
            Console.WriteLine($"   → Stocks: {stocks.Count}");

            var subject = $"[NEO] Stock Fetch {status} - {businessDate:yyyy-MM-dd}";

            var sectorSummary = stocks
                .GroupBy(s => s.SectorName)
                .OrderBy(g => g.Key)
                .Select(g => $"<tr><td>{g.Key}</td><td>{g.Count()}</td></tr>")
                .ToList();

            var sectorRows = sectorSummary.Count > 0
                ? string.Join("", sectorSummary)
                : "<tr><td colspan='2'>No stocks inserted</td></tr>";

            var htmlBody = $@"
                <html>
                <body style='font-family: Arial, sans-serif;'>
                    <h2>ProjectNEO Stock Fetch Summary</h2>

                    <p><b>Status:</b> {status}</p>
                    <p><b>Business Date:</b> {businessDate:yyyy-MM-dd}</p>
                    <p><b>Run ID:</b> {runId}</p>
                    <p><b>Total Stocks:</b> {stocks.Count}</p>
                    <p><b>Message:</b> {message}</p>

                    <h3>Sector Summary</h3>
                    <table border='1' cellpadding='6' cellspacing='0'>
                        <tr>
                            <th>Sector</th>
                            <th>Stock Count</th>
                        </tr>
                        {sectorRows}
                    </table>

                    <br/>
                    <p>This is an automated ProjectNEO stock-fetch notification.</p>
                </body>
                </html>";

            var email = new MimeKit.MimeMessage();
            email.From.Add(MimeKit.MailboxAddress.Parse(_fromEmail));
            email.To.Add(MimeKit.MailboxAddress.Parse(_toEmail));
            email.Subject = subject;

            email.Body = new MimeKit.BodyBuilder
            {
                HtmlBody = htmlBody,
                TextBody =
                    $"ProjectNEO Stock Fetch {status}\n" +
                    $"Business Date: {businessDate:yyyy-MM-dd}\n" +
                    $"Run ID: {runId}\n" +
                    $"Total Stocks: {stocks.Count}\n" +
                    $"Message: {message}"
            }.ToMessageBody();

            using var smtp = new MailKit.Net.Smtp.SmtpClient();

            await smtp.ConnectAsync(
                _smtpHost,
                _smtpPort,
                MailKit.Security.SecureSocketOptions.StartTls);

            await smtp.AuthenticateAsync(_fromEmail, _fromPassword);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);

            Console.WriteLine("   ✅ Stock fetch summary email sent successfully!");
        }
    }
}

