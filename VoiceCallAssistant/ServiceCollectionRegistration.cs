using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using Serilog;
using System.Configuration;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Repository;
using VoiceCallAssistant.Services;

namespace VoiceCallAssistant;

public static class ServiceCollectionRegistration
{
    public static IServiceCollection AddLogging(this IServiceCollection services, ILoggingBuilder logging, IConfiguration configuration)
    {
        logging.ClearProviders();

        services.AddOpenTelemetry().WithTracing(tracing =>
        {
            tracing.AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();
        })
        .UseAzureMonitorExporter(options => options.ConnectionString = configuration.GetRequiredSection("AzureMonitor").GetSection("ConnectionString").Value);

        services.AddSerilog((services, lc) => lc
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext(), 
            writeToProviders: true);

        return services;
    }
    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<ITwilioService, TwilioService>();
        services.AddScoped<IRealtimeAiService, RealtimeAiService>();
        services.AddScoped<IRepository, EfRepository>();

        return services;
    }

    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseConfig = configuration.GetRequiredSection(DatabaseConfig.SectionName).Get<DatabaseConfig>();
        if (databaseConfig == null)
        {
            throw new ConfigurationErrorsException($"Configuration section '{DatabaseConfig.SectionName}' is missing or invalid.");
        }

        services.AddDbContext<AppDbContext>(options => 
            options.UseCosmos(
                databaseConfig.ConnectionString,
                databaseConfig.Name,
                options =>
                {
                    options.ConnectionMode(ConnectionMode.Gateway);
                    options.GatewayModeMaxConnectionLimit(12);
                }));

        return services;
    }
}
