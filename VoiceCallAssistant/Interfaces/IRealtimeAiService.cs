using OpenAI.RealtimeConversation;

namespace VoiceCallAssistant.Interfaces;

public interface IRealtimeAIService
{
    Task<RealtimeConversationSession> CreateConversationSessionAsync(CancellationTokenSource cts, string? systemMessage = null, ConversationSessionOptions? conversationSessionOptions = null);
    Task CloseRealtime(RealtimeConversationSession session, CancellationToken cancellationToken);
    Task CloseRealtimeWithError(RealtimeConversationSession session, string message, CancellationToken cancellationToken);
}