using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using VoiceCallAssistant.Interfaces;

namespace VoiceCallAssistant.Controllers;

[ApiController]
public class MediaStreamController : ControllerBase
{
    private readonly ITwilioService _twilioService;
    //private readonly IRealTimeOpenAiService _aiService;

    public MediaStreamController(
        ITwilioService twilioService)
        //IRealTimeOpenAiService aiService)
    {
        _twilioService = twilioService;
       // _aiService = aiService;
    }


    [HttpGet("media-stream", Name = "MediaStreamWebsocket")]
    public async Task Get()
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
        long? responseStartTs = null;

        // Kick off both loops
        var receiveTask = ReceiveFromTwilio(webSocket, cts.Token,
            sid => streamSid = sid,
            (audio, ts) => {
                latestTimestamp = ts;
                //_aiService.SendAudioChunkAsync(audio, cts.Token).GetAwaiter().GetResult();
                Console.WriteLine($"Latest Timestamp: {ts}");
            });

        /*
        var sendTask = SendToTwilio(webSocket, cts.Token,
            async aiMsg =>
            {
                // audio delta
                var delta = aiMsg.AudioDelta; // byte[]
                var payloadB64 = Convert.ToBase64String(delta);

                // once on first audio, capture start timestamp
                responseStartTs ??= latestTimestamp;
                if (aiMsg.ItemId is not null)
                    lastAssistantId = aiMsg.ItemId;

                // send media event
                var mediaObj = new
                {
                    @event = "media",
                    streamSid = streamSid,
                    media = new { payload = payloadB64 }
                };
                await webSocket.SendAsync(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mediaObj)),
                    WebSocketMessageType.Text, true, CancellationToken.None);

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
                    WebSocketMessageType.Text, true, CancellationToken.None);
            },
            async (aiMsg) =>
            {
                // on speech_started event
                if (markQueue.TryDequeue(out _) && responseStartTs.HasValue && lastAssistantId is not null)
                {
                    var elapsed = latestTimestamp - responseStartTs.Value;
                    await _aiService.SendTruncateMessageAsync(lastAssistantId, elapsed, cts.Token);

                    // clear signal back to Twilio
                    var clearObj = new { @event = "clear", streamSid = streamSid };
                    await webSocket.SendAsync(
                        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(clearObj)),
                        WebSocketMessageType.Text, true, CancellationToken.None);

                    responseStartTs = null;
                    lastAssistantId = null;
                }
            });
        */

        await Task.WhenAll(receiveTask); //, sendTask);
        cts.Cancel();
    }


    private async Task ReceiveFromTwilio(
            WebSocket webSocket,
            CancellationToken ct,
            Action<string> setStreamSid,
            Action<byte[], long> handleAudio)
    {
        var buffer = new byte[4 * 1024];

        while (!ct.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer), ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "closing", CancellationToken.None);
                break;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var evt = root.GetProperty("event").GetString();

            switch (evt)
            {
                case "start":
                    {
                        var sid = root
                            .GetProperty("start")
                            .GetProperty("streamSid")
                            .GetString()!;
                        setStreamSid(sid);
                        break;
                    }
                case "media":
                    {
                        var payloadB64 = root
                            .GetProperty("media")
                            .GetProperty("payload")
                            .GetString()!;
                        var ts = root
                            .GetProperty("media")
                            .GetProperty("timestamp")
                            .GetInt64();
                        var audioBytes = Convert.FromBase64String(payloadB64);
                        handleAudio(audioBytes, ts);
                        break;
                    }
                case "mark":
                    {
                        // Twilio ack: you could dequeue here if you like
                        break;
                    }
            }
        }
    }

    /*
    private async Task SendToTwilio(
        WebSocket webSocket,
        CancellationToken ct,
        Func<RealTimeAiMessage, Task> handleAudioDelta,
        Func<RealTimeAiMessage, Task> handleSpeechStarted)
    {
        await foreach (var aiMsg in _aiService.StreamResponsesAsync(ct))
        {
            if (aiMsg.Type == RealTimeAiMessageType.AudioDelta)
            {
                await handleAudioDelta(aiMsg);
            }
            else if (aiMsg.Type == RealTimeAiMessageType.SpeechStarted)
            {
                await handleSpeechStarted(aiMsg);
            }
        }
    }
    */

    // Simple DTO for passing AI messages around
    public record RealTimeAiMessage(
        RealTimeAiMessageType Type,
        byte[]? AudioDelta,
        string? ItemId);

    public enum RealTimeAiMessageType
    {
        AudioDelta,
        SpeechStarted,
        // … add others if you need
    }
}

