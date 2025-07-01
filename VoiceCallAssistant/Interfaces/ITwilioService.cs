using Microsoft.AspNetCore.Mvc.Rendering;
using OpenAI.RealtimeConversation;
using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace VoiceCallAssistant.Interfaces;

public interface ITwilioService
{
    public void CreateClient();
    public string MakeCall(string toPhoneNumber, string routineId);
    public string ConnectWebhook(string toPhoneNumber);
    public bool ValidateRequest(string url, IHeaderDictionary headers, IFormCollection form);
}
