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
        var receiveTask = ReceiveFromTwilio(webSocket, cts.Token,
            sid => streamSid = sid,
            async (audio, ts) => {
            latestTimestamp = ts;
            await session.SendInputAudioAsync(audio, cts.Token);
            },
            markQueue);
        
        var sendTask = SendToTwilio(session, cts.Token,
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


    private async Task ReceiveFromTwilio(
            WebSocket webSocket,
            CancellationToken ct,
            Action<string> setStreamSid,
            Action<BinaryData, long> handleAudio,
            ConcurrentQueue<string> markQueue)
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

            var jsonString = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;
            var evt = root.GetProperty("event").GetString();

            switch (evt)
            {
                case "start":
                    {
                        var sid = ExtractStreamSid(root);

                        setStreamSid(sid);
                        break;
                    }
                case "media":
                    {
                        var (audioBinary, tsLong) = ExtractPayload(root);

                        handleAudio(audioBinary, tsLong);
                        break;
                    }
                case "mark":
                    {
                        markQueue.TryDequeue(out _);
                        break;
                    }
                case "stop":
                    {
                        await webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "closing", CancellationToken.None);
                        break;
                    }
            }
        }
    }

    private async Task SendToTwilio(RealtimeConversationSession session,
        CancellationToken ct,
        Func<ConversationItemStreamingPartDeltaUpdate, Task> handleAudioDelta,
        Func<ConversationInputSpeechStartedUpdate, Task> handleSpeechStarted)
    {
        await foreach (ConversationUpdate update in session.ReceiveUpdatesAsync(ct))
        {
            // Notification indicating the start of the conversation session.
            if (update is ConversationSessionStartedUpdate sessionStartedUpdate)
            {
                // Start conversation first
                await session.StartResponseAsync();

                Console.WriteLine($"<<< Session started. ID: {sessionStartedUpdate.SessionId}");                
                Console.WriteLine();
            }

            // Notification indicating the start of detected voice activity.
            if (update is ConversationInputSpeechStartedUpdate speechStartedUpdate)
            {
                Console.WriteLine(
                    $"  -- Voice activity detection started at {speechStartedUpdate.AudioStartTime}");
                await handleSpeechStarted(speechStartedUpdate);
            }

            // Notification indicating the end of detected voice activity.
            if (update is ConversationInputSpeechFinishedUpdate speechFinishedUpdate)
            {
                Console.WriteLine(
                    $"  -- Voice activity detection ended at {speechFinishedUpdate.AudioEndTime}");
            }

            // Notification about item streaming delta, which may include audio transcript, audio bytes, or function arguments.
            if (update is ConversationItemStreamingPartDeltaUpdate deltaUpdate)
            {

                // Handle audio bytes.
                if (deltaUpdate.AudioBytes is not null)
                {
                    await handleAudioDelta(deltaUpdate);
                }
            }

            // Notification indicating the completion of transcription from input audio.
            if (update is ConversationInputTranscriptionFinishedUpdate transcriptionCompletedUpdate)
            {
                Console.WriteLine();
                Console.WriteLine($"  -- User audio transcript: {transcriptionCompletedUpdate.Transcript}");
                Console.WriteLine();
            }

            // Notification about error in conversation session.
            if (update is ConversationErrorUpdate errorUpdate)
            {
                Console.WriteLine();
                Console.WriteLine($"ERROR: {errorUpdate.Message}");
                break;
            }
        }
    }

    private (BinaryData audioBinary, long ts) ExtractPayload(JsonElement root)
    {
        var payloadB64 = root
            .GetProperty("media")
            .GetProperty("payload")
            .GetString()!;
        var ts = root
            .GetProperty("media")
            .GetProperty("timestamp")
            .GetString();
        var audioBytes = Convert.FromBase64String(payloadB64);
        var audioBinary = new BinaryData(audioBytes);
        var tsLong = Convert.ToInt64(ts);

        return (audioBinary, tsLong);
    }

    private string ExtractStreamSid(JsonElement root)
    {
        return root
            .GetProperty("start")
            .GetProperty("streamSid")
            .GetString()!;
    }
}

