using NEO.Core.Services;
using NEO.Web.Workers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

// ── Register Background Scheduler ──
builder.Services.AddHostedService<PipelineScheduler>();

var app = builder.Build();

// ── Run pipeline once on startup ──
var connStr = builder.Configuration
                          .GetConnectionString("DefaultConnection")!;
var apiKey = builder.Configuration["AppSettings:AlphaVantageApiKey"]!;
var smtpHost = builder.Configuration["EmailSettings:SmtpHost"] ?? "smtp.gmail.com";
var smtpPort = int.Parse(builder.Configuration["EmailSettings:SmtpPort"] ?? "587");
var fromEmail = builder.Configuration["EmailSettings:FromEmail"] ?? "";
var fromPassword = builder.Configuration["EmailSettings:FromPassword"] ?? "";
var toEmail = builder.Configuration["EmailSettings:ToEmail"] ?? "";

Console.WriteLine("Starting ProjectNEO...");

var orchestrator = new PipelineOrchestrator(
    connStr, apiKey,
    smtpHost, smtpPort,
    fromEmail, fromPassword, toEmail);

await orchestrator.RunDailyPipelineAsync();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();