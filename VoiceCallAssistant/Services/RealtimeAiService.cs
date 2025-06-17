using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using OpenAI.RealtimeConversation;
using System.ClientModel;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Models;

namespace VoiceCallAssistant.Services;

public class RealtimeAiService : IRealtimeAiService
{
    private readonly IConfiguration _configuration;

    public RealtimeAiService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // Should I use using when calling this method?
    public async Task<RealtimeConversationSession> CreateConversationSessionAsync(
        CancellationTokenSource cts,  
        string? systemMessage = null,
        ConversationSessionOptions? conversationSessionOptions = null)
    {
        var realtimeClient = GetRealtimeConversationClient();
        var kernel = Kernel.CreateBuilder().Build();

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
                Instructions = "You are profile number 1845. You will be talking in English with foreignes, you can expect funny accent.",
                TurnDetectionOptions = ConversationTurnDetectionOptions.CreateServerVoiceActivityTurnDetectionOptions(
                    silenceDuration: new TimeSpan(0,0,0,0,500))
            };
        } 

        // Configure session with defined options.
        await session.ConfigureSessionAsync(conversationSessionOptions, cts.Token);

        if (!string.IsNullOrEmpty(systemMessage))
        {
            await session.AddItemAsync(
                ConversationItem.CreateSystemMessage([$"{systemMessage}"]), cts.Token);
        }

        return session;
    }

    private RealtimeConversationClient GetRealtimeConversationClient()
    {
        var openAIOptions = _configuration.GetSection(OpenAIOptions.SectionName).Get<OpenAIOptions>();
        //var azureOpenAIOptions = _configuration.GetSection(AzureOpenAIOptions.SectionName).Get<AzureOpenAIOptions>();

        if (openAIOptions is not null && openAIOptions.IsValid)
        {
            return new RealtimeConversationClient(
                model: openAIOptions.Model,
                credential: new ApiKeyCredential(openAIOptions.ApiKey));
        }
        //else if (azureOpenAIOptions is not null && azureOpenAIOptions.IsValid)
        //{
        //    var client = new AzureOpenAIClient(
        //        endpoint: new Uri(azureOpenAIOptions.Endpoint),
        //        credential: new ApiKeyCredential(azureOpenAIOptions.ApiKey));

        //    return client.GetRealtimeConversationClient(azureOpenAIOptions.DeploymentName);
        //}
        else
        {
            throw new Exception("OpenAI/Azure OpenAI configuration was not found.");
        }
    }
}
