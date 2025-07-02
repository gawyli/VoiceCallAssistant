using System.Net.WebSockets;

namespace VoiceCallAssistant.Interfaces;

public interface IVoiceCallService
{
    Task OrchestrateAsync(WebSocket websocket, string userPrompt, CancellationTokenSource cancellationTokenSource);
}
