using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using System.Configuration;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Repository;
using VoiceCallAssistant.Services;

namespace VoiceCallAssistant;

public static class ServiceCollectionRegistration
{
    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<ITwilioService, TwilioService>();
        services.AddScoped<IRealtimeAiService, RealtimeAiService>();
        services.AddScoped<IRepository, EfRepository>();

        return services;
    }

    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var cosmosDbConfig = configuration.GetRequiredSection(CosmosDbConfig.SectionName).Get<CosmosDbConfig>();
        if (cosmosDbConfig == null)
        {
            throw new ConfigurationErrorsException($"Configuration section '{CosmosDbConfig.SectionName}' is missing or invalid.");
        }

        services.AddDbContext<CosmosDbContext>(options => options.UseCosmos(
            cosmosDbConfig.ConnectionString,
            cosmosDbConfig.Name,
            options =>
            {
                options.ConnectionMode(ConnectionMode.Gateway);
                options.GatewayModeMaxConnectionLimit(12);
                /*options.LimitToEndpoint();
                options.GatewayModeMaxConnectionLimit(32);
                options.MaxRequestsPerTcpConnection(8);
                options.MaxTcpConnectionsPerEndpoint(16);
                options.IdleTcpConnectionTimeout(TimeSpan.FromMinutes(1));
                options.OpenTcpConnectionTimeout(TimeSpan.FromMinutes(1));
                options.RequestTimeout(TimeSpan.FromMinutes(1));*/
            }));

        

       
        return services;
    }
}
