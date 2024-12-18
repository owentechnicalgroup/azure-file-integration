using FileReceiverService;
using Microsoft.ApplicationInsights.Extensibility;
using Serilog;
using Serilog.Events;

var builder = Host.CreateDefaultBuilder(args);

// Get configuration early
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

builder.ConfigureServices((hostContext, services) =>
{
    // Add Application Insights only if connection string is configured
    var appInsightsConnectionString = configuration["ApplicationInsights:ConnectionString"];
    if (!string.IsNullOrEmpty(appInsightsConnectionString))
    {
        services.AddApplicationInsightsTelemetryWorkerService();
    }
    services.AddHostedService<Worker>();
});

// Configure Serilog
var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console();

// Add Application Insights sink only if connection string is configured
var appInsightsConnectionString = configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrEmpty(appInsightsConnectionString))
{
    loggerConfig.WriteTo.ApplicationInsights(
        new TelemetryConfiguration { ConnectionString = appInsightsConnectionString },
        TelemetryConverter.Traces);
}

Log.Logger = loggerConfig.CreateLogger();

try
{
    builder.UseSerilog();
    
    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
