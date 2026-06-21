using NEO.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddNeoCoreServices(builder.Configuration);

// IMPORTANT:
// Web host should NOT run the pipeline automatically.
// Azure Functions (DailyRunFunction) is the ONLY production scheduler.

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();

Console.WriteLine("ProjectNEO Web host started.");
Console.WriteLine("Pipeline execution is disabled in Web host.");
Console.WriteLine("Azure Functions DailyRunFunction is the only production scheduler.");

app.Run();