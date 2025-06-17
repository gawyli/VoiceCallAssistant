using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using OpenAI.RealtimeConversation;
using System.ClientModel;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Models;

namespace VoiceCallAssistant.Controllers;

[ApiController]
[Route("/ws")]
public class MediaStreamController : ControllerBase
{
    private readonly ITwilioService _twilioService;
    private readonly IRealtimeAiService _realtimeAiService;
    private readonly IConfiguration _configuration;

    public MediaStreamController(ITwilioService twilioService, IRealtimeAiService realtimeAiService, IConfiguration configuration)
    {
        _twilioService = twilioService;
        _realtimeAiService = realtimeAiService;
        _configuration = configuration;

    }

    [HttpGet("media-stream", Name = "MediaStreamWebsocket")]
    public async Task MediaStreamWebsocketGet()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await HandleMediaStream(webSocket);
    }

    private async Task HandleMediaStream(WebSocket webSocket)
    {
        var cts = new CancellationTokenSource();
        var markQueue = new ConcurrentQueue<string>();

        string? streamSid = null;
        long latestTimestamp = 0;
        string? lastAssistantId = null;
        int? contentPartsIndex = null;
        long? responseStartTs = null;

        string systemMessage = "Welcome a user with your profile number.";
        using var session = await _realtimeAiService.CreateConversationSessionAsync(cts, systemMessage);

        // Kick off both loops
        var receiveTask = _twilioService.ReceiveFrom(webSocket, cts.Token,
            sid => streamSid = sid,
            async (audio, ts) => {
            latestTimestamp = ts;
            await session.SendInputAudioAsync(audio, cts.Token);
            },
            markQueue);
        
        var sendTask = _twilioService.SendTo(session, cts.Token,
            async partDelta =>
            {
                // audio delta
                var delta = partDelta.AudioBytes;
                var payloadB64 = Convert.ToBase64String(delta);

                // once on first audio, capture start timestamp
                responseStartTs ??= latestTimestamp;

                lastAssistantId = partDelta.ItemId;
                contentPartsIndex = partDelta.ContentPartIndex;                   

                // send media event
                var mediaObj = new
                {
                    @event = "media",
                    streamSid = streamSid,
                    media = new { payload = payloadB64 }
                };
                await webSocket.SendAsync(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mediaObj)),
                    WebSocketMessageType.Text, true, cts.Token);

                // send mark
                markQueue.Enqueue("responsePart");
                var markObj = new
                {
                    @event = "mark",
                    streamSid = streamSid,
                    mark = new { name = "responsePart" }
                };
                await webSocket.SendAsync(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(markObj)),
                    WebSocketMessageType.Text, true, cts.Token);
            },
            async (speechStarted) =>
            {
                // on speech_started event
                if (markQueue.Any() && responseStartTs.HasValue && lastAssistantId is not null)
                {
                    var elapsed = new TimeSpan(latestTimestamp - responseStartTs.Value);
                    await session.TruncateItemAsync(lastAssistantId, contentPartsIndex!.Value, elapsed, cts.Token);

                    // clear signal back to Twilio
                    var clearObj = new { @event = "clear", streamSid = streamSid };
                    await webSocket.SendAsync(
                        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(clearObj)),
                        WebSocketMessageType.Text, true, cts.Token);

                    markQueue.Clear();
                    responseStartTs = null;
                    lastAssistantId = null;
                    contentPartsIndex = null;
                }
            });

        await Task.WhenAll(receiveTask, sendTask);
        cts.Cancel();
    }
}

