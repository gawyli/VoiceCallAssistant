using System.Net.WebSockets;
using OpenAI.RealtimeConversation;

namespace VoiceCallAssistant.Interfaces;

public interface IVoiceCallService
{
    Task<RealtimeConversationSession> CreateConversationSession(string userPrompt, CancellationTokenSource cancellationTokenSource);
    Task OrchestrateExchangeAsync(WebSocket webSocket1, RealtimeConversationSession webSocket2, CancellationTokenSource cancellationTokenSource);
}
