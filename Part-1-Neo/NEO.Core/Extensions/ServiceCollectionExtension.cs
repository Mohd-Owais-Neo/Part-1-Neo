using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NEO.Core.Data;
using NEO.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddNeoCoreServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var connStr = configuration.GetConnectionString("DefaultConnection")
                         ?? throw new Exception("Connection string 'DefaultConnection' not found.");

            var apiKey = configuration["AppSettings:AlphaVantageApiKey"]
                         ?? throw new Exception("AppSettings:AlphaVantageApiKey not found.");

            var smtpHost = configuration["EmailSettings:SmtpHost"] ?? "smtp.gmail.com";
            var smtpPort = int.Parse(configuration["EmailSettings:SmtpPort"] ?? "587");
            var fromEmail = configuration["EmailSettings:FromEmail"] ?? "";
            var fromPassword = configuration["EmailSettings:FromPassword"] ?? "";
            var toEmail = configuration["EmailSettings:ToEmail"] ?? "";

            services.AddSingleton(new DatabaseHelper(connStr));
            services.AddSingleton(new ApiDataService(apiKey));
            services.AddSingleton<IntersectionService>();
            services.AddSingleton<StockFilterService>();
            services.AddSingleton<MarketDataService>();
            services.AddSingleton<TopStockSelectorService>();
            services.AddSingleton<RiskManagementService>();
            services.AddSingleton(new EmailAlertService(
                smtpHost,
                smtpPort,
                fromEmail,
                fromPassword,
                toEmail));

            services.AddSingleton<PipelineOrchestrator>();

            return services;
        }
    }
}

