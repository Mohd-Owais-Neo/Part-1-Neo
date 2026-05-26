using NEO.Core.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();
var app = builder.Build();

// -----------------------------------------------
// PIPELINE — DB MODE (uses cached sector data)
// -----------------------------------------------
var connStr = builder.Configuration
                     .GetConnectionString("DefaultConnection")!;
var apiKey = builder.Configuration["AppSettings:AlphaVantageApiKey"]!;

Console.WriteLine("Starting ProjectNEO (DB Mode)...");

var orchestrator = new PipelineOrchestrator(connStr, apiKey);
await orchestrator.RunFromDbDataAsync();
// -----------------------------------------------

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();