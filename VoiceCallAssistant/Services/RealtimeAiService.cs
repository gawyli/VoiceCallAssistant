using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using OpenAI.RealtimeConversation;
using System.ClientModel;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Models;
using ILogger = Serilog.ILogger;

namespace VoiceCallAssistant.Services;

public class RealtimeAIService : IRealtimeAIService
{
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;

    public RealtimeAIService(ILogger logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<RealtimeConversationSession> CreateConversationSessionAsync(
        CancellationTokenSource cts,  
        string? systemMessage = null,
        ConversationSessionOptions? conversationSessionOptions = null)
    {
        _logger.Information("Starting to create a conversation session.");
        
        var realtimeClient = GetRealtimeConversationClient();
        var kernel = Kernel.CreateBuilder().Build(); // TODO: Check if this is needed or can be removed.

        RealtimeConversationSession session = await realtimeClient.StartConversationSessionAsync(cts.Token);

        if (conversationSessionOptions == null)
        {
            conversationSessionOptions = new()
            {
                Voice = ConversationVoice.Ash,
                InputAudioFormat = ConversationAudioFormat.G711Ulaw,
                OutputAudioFormat = ConversationAudioFormat.G711Ulaw,
                InputTranscriptionOptions = new()
                {
                    Model = "whisper-1"
                }, 
                TurnDetectionOptions = ConversationTurnDetectionOptions.CreateServerVoiceActivityTurnDetectionOptions(
                    silenceDuration: new TimeSpan(0,0,0,0,500))
            };
        } 

        // Configure session with defined options.
        await session.ConfigureSessionAsync(conversationSessionOptions, cts.Token);
        _logger.Information("Session configured successfully.");

        if (!string.IsNullOrEmpty(systemMessage))
        {
            await session.AddItemAsync(
                ConversationItem.CreateSystemMessage([$"{systemMessage}"]), cts.Token);
            _logger.Information("System message added to session.");
        }

        return session;
    }

    private RealtimeConversationClient GetRealtimeConversationClient()
    {
        var openAIOptions = _configuration.GetSection(OpenAIOptions.SectionName).Get<OpenAIOptions>();
        var azureOpenAIOptions = _configuration.GetSection(AzureOpenAIOptions.SectionName).Get<AzureOpenAIOptions>();

        if (openAIOptions is not null && openAIOptions.IsValid)
        {
            return new RealtimeConversationClient(
                model: openAIOptions.Model,
                credential: new ApiKeyCredential(openAIOptions.ApiKey!));
        }
        else if (azureOpenAIOptions is not null && azureOpenAIOptions.IsValid)
        {
            var client = new AzureOpenAIClient(
                endpoint: new Uri(azureOpenAIOptions.Endpoint!),
                credential: new ApiKeyCredential(azureOpenAIOptions.ApiKey!));

            return client.GetRealtimeConversationClient(azureOpenAIOptions.DeploymentName);
        }
        else
        {
            _logger.Fatal("Failed to create RealtimeConversationClient. Configuration was not found.");
            throw new Exception("OpenAI/Azure OpenAI configuration was not found.");
        }
    }
}
