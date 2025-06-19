using Microsoft.AspNetCore.Mvc.Rendering;
using OpenAI.RealtimeConversation;
using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace VoiceCallAssistant.Interfaces;

public interface ITwilioService
{
    public void CreateClient();
    public string MakeCall(string toPhoneNumber);
    public string ConnectWebhook(string toPhoneNumber);
    public bool ValidateRequest(HttpRequest request);

    public Task ReceiveFrom(
                WebSocket webSocket,
                CancellationToken ct,
                Action<string> setStreamSid,                
                Action<BinaryData, long> handleAudio,
                ConcurrentQueue<string> markQueue);

    public Task SendTo(RealtimeConversationSession session,
        CancellationToken ct,
        Func<ConversationItemStreamingPartDeltaUpdate, Task> handleAudioDelta,
        Func<ConversationInputSpeechStartedUpdate, Task> handleSpeechStarted);
}
