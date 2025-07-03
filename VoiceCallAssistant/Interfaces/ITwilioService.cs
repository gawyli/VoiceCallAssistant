using System.Net.WebSockets;
using VoiceCallAssistant.Models;
using VoiceCallAssistant.Models.Events;

namespace VoiceCallAssistant.Interfaces;

public interface ITwilioService
{
    void CreateClient();
    string MakeCall(string toPhoneNumber, string routineId);
    string ConnectWebhook(string toPhoneNumber);
    bool ValidateRequest(string url, IHeaderDictionary headers, IFormCollection form);
    Task ClearQueue(WebSocket webSocket, string streamSid, CancellationToken cancellationToken);
    Task SendInputAudioAsync(WebSocket webSocket, string payloadB64, CallState state, CancellationToken cancellationToken);
    IAsyncEnumerable<TwilioEvent> ReceiveUpdatesAsync(WebSocket webSocket, CancellationToken cancellationToken);
    Task CloseTwilio(WebSocket webSocket, CancellationToken cancellationToken);
    Task CloseTwilioWithError(WebSocket webSocket, string message, CancellationToken cancellationToken);
}
