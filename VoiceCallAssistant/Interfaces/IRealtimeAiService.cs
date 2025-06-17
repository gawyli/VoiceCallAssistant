using OpenAI.RealtimeConversation;

namespace VoiceCallAssistant.Interfaces;

public interface IRealtimeAiService
{
    Task<RealtimeConversationSession> CreateConversationSessionAsync(CancellationTokenSource cts, string? systemMessage = null, ConversationSessionOptions? conversationSessionOptions = null);
}