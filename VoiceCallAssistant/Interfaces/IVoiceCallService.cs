using System.Net.WebSockets;
using OpenAI.RealtimeConversation;

namespace VoiceCallAssistant.Interfaces;

public interface IVoiceCallService
{
    Task<RealtimeConversationSession> CreateConversationSession(string userPrompt, CancellationTokenSource cancellationTokenSource);
    Task OrchestrateAsync(WebSocket webSocket1, RealtimeConversationSession webSocket2, CancellationTokenSource cancellationTokenSource);
}
