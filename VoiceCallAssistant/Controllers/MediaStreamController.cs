using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using OpenAI.RealtimeConversation;
using System.ClientModel;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Models;
using VoiceCallAssistant.Utilities;

namespace VoiceCallAssistant.Controllers;

[ApiController]
[Route("/ws")]
public class MediaStreamController : ControllerBase
{
    private readonly IRepository _repository;
    private readonly ITwilioService _twilioService;
    private readonly IRealtimeAiService _realtimeAiService;
    private readonly IConfiguration _configuration;

    public MediaStreamController(IRepository repository, ITwilioService twilioService, IRealtimeAiService realtimeAiService, IConfiguration configuration)
    {
        _repository = repository;
        _twilioService = twilioService;
        _realtimeAiService = realtimeAiService;
        _configuration = configuration;

    }

    [HttpGet("media-stream/{routineId}", Name = "MediaStreamWebsocket")]
    public async Task MediaStreamWebsocketGet()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // TODO: Research how validate websocket
        // if (!_twilioService.ValidateRequest(this.Request))
        // {
        //     HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        //     return;
        // }

        var routineId = this.Request.Path.GetLastItem('/');
        if (string.IsNullOrEmpty(routineId))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        string systemMessage =
        """
        You are wake up call asistant. Your instructions are specifided between markup <PresonlalisedPrompt></PresonlalisedPrompt>. 
        Follow the instructions in the conversation you have with the user.

        """;

        var routine = await _repository.GetByIdAsync<Routine>(routineId, this.HttpContext.RequestAborted);
        if (routine != null)
        {
            // TODO: Add Interests and Tasks
            systemMessage += $"<PersonalisedPrompt> {routine.Preferences.PersonalisedPrompt} </PersonalisedPrompt>";
        }

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await HandleMediaStream(webSocket, systemMessage);
    }

    private async Task HandleMediaStream(WebSocket webSocket, string systemMessage)
    {
        var cts = new CancellationTokenSource();
        // Set reasonable timeout
        cts.CancelAfter(TimeSpan.FromMinutes(30));

        try
        {
            var state = new CallState
            {
                StreamSid = null,
                LatestTimestamp = 0,
                LastAssistantId = null,
                ContentPartsIndex = null,
                ResponseStartTs = null,
                MarkQueue = new ConcurrentQueue<string>()
            };

            using var session = await _realtimeAiService.CreateConversationSessionAsync(cts, systemMessage);

            await ProcessWebSocketConnection(webSocket, session, state, cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing WebSocket: {ex.InnerException}");

            await CloseWebSocketWithError(webSocket, "Internal server error occurred");          
        }
        finally
        {
            cts.Cancel();
        }
    }

    private async Task CloseWebSocketWithError(WebSocket webSocket, string message)
    {
        if (webSocket.State == WebSocketState.Open)
        {
            await webSocket.CloseAsync(
                    WebSocketCloseStatus.InternalServerError,
                    message,
                    CancellationToken.None);
        }   
    }

    private async Task ProcessWebSocketConnection(
        WebSocket webSocket,
        RealtimeConversationSession session, 
        CallState state, 
        CancellationToken ct)
    {
        var receiveTask = ProcessIncomingMessages(webSocket, session, state, ct);
        var sendTask = ProcessOutgoingMessages(webSocket, session, state, ct);

        await Task.WhenAll(receiveTask, sendTask);
    }

    private async Task ProcessIncomingMessages(
        WebSocket webSocket,
        RealtimeConversationSession session, 
        CallState state, 
        CancellationToken ct)
    {
        var buffer = new byte[4 * 1024];

        while (!ct.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            try
            {
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Client closed connection",
                        CancellationToken.None);
                    break;
                }

                await ProcessIncomingMessage(buffer, result.Count, webSocket, session, state, ct);
            }
            catch (WebSocketException ex)
            {
                Console.WriteLine($"WebSocket error occurred: {ex.InnerException}");
                break;
            }
        }
    }

    private async Task ProcessIncomingMessage(
        byte[] buffer, 
        int count,
        WebSocket webSocket,
        RealtimeConversationSession session, 
        CallState state, 
        CancellationToken ct)
    {
        var jsonString = Encoding.UTF8.GetString(buffer, 0, count);
        using var doc = JsonDocument.Parse(jsonString);
        var root = doc.RootElement;

        if (!root.TryGetProperty("event", out var eventProp))
            return;

        var eventType = eventProp.GetString();

        switch (eventType)
        {
            case "start":
                HandleStartEvent(root, state);
                break;

            case "media":
                var audioBinary = HandleMediaEvent(root, state);
                await session.SendInputAudioAsync(audioBinary);
                break;

            case "mark":
                state.MarkQueue.TryDequeue(out _);
                break;

            case "stop":
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Stream ended",
                    CancellationToken.None);
                break;
        }
    }

    private void HandleStartEvent(
        JsonElement root, 
        CallState state)
    {
        state.StreamSid = root
                .GetProperty("start")
                .GetProperty("streamSid")
                .GetString()!;

        Console.WriteLine($"Stream started with SID: {state.StreamSid}");
    }

    private BinaryData HandleMediaEvent(
        JsonElement root,  
        CallState state)
    {
        var payloadB64 = root
            .GetProperty("media")
            .GetProperty("payload")
            .GetString()!;

        var timestampStr = root
            .GetProperty("media")
            .GetProperty("timestamp")
            .GetString()!;

        var audioBytes = Convert.FromBase64String(payloadB64);
        var audioBinary = new BinaryData(audioBytes);
        var timestamp = Convert.ToInt64(timestampStr);

        state.LatestTimestamp = timestamp;

        return audioBinary;
    }

    private async Task ProcessOutgoingMessages(
        WebSocket webSocket,
        RealtimeConversationSession session, 
        CallState state, 
        CancellationToken ct)
    {
        try
        {
            await foreach (ConversationUpdate update in session.ReceiveUpdatesAsync(ct))
            {
                if (ct.IsCancellationRequested)
                    break;

                // Session start - initialize the response
                if (update is ConversationSessionStartedUpdate sessionStartedUpdate)
                {
                    await session.StartResponseAsync();
                    Console.WriteLine($"Session started: {sessionStartedUpdate.SessionId}");
                    continue;
                }

                // Process incoming speech detection (used for barge-in)
                if (update is ConversationInputSpeechStartedUpdate speechStartedUpdate)
                {
                    Console.WriteLine($"Speech detection started at {speechStartedUpdate.AudioStartTime}");

                    if (state.MarkQueue.Any() && state.ResponseStartTs.HasValue && state.LastAssistantId != null)
                    {
                        var elapsed = new TimeSpan(state.LatestTimestamp - (long)state.ResponseStartTs);
                        await session.TruncateItemAsync(
                            state.LastAssistantId.ToString()!,
                            (int)state.ContentPartsIndex!,
                            elapsed,
                            ct);

                        var clearObj = new { @event = "clear", streamSid = state.StreamSid };
                        await webSocket.SendAsync(
                            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(clearObj)),
                            WebSocketMessageType.Text,
                            true,
                            ct);

                        state.MarkQueue.Clear();
                        state.ResponseStartTs = null;
                        state.LastAssistantId = null;
                        state.ContentPartsIndex = null;
                    }
                    continue;
                }

                if (update is ConversationInputSpeechFinishedUpdate speechFinishedUpdate)
                {
                    Console.WriteLine($"Speech detection ended at {speechFinishedUpdate.AudioEndTime}");
                    continue;
                }

                if (update is ConversationItemStreamingPartDeltaUpdate deltaUpdate && deltaUpdate.AudioBytes != null)
                {
                    var delta = deltaUpdate.AudioBytes;
                    var payloadB64 = Convert.ToBase64String(delta);

                    state.ResponseStartTs ??= state.LatestTimestamp;

                    state.LastAssistantId = deltaUpdate.ItemId;
                    state.ContentPartsIndex = deltaUpdate.ContentPartIndex;

                    var mediaObj = new
                    {
                        @event = "media",
                        streamSid = state.StreamSid,
                        media = new { payload = payloadB64 }
                    };
                    await webSocket.SendAsync(
                        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mediaObj)),
                        WebSocketMessageType.Text, true, ct);

                    state.MarkQueue.Enqueue("responsePart");
                    var markObj = new
                    {
                        @event = "mark",
                        streamSid = state.StreamSid,
                        mark = new { name = "responsePart" }
                    };
                    await webSocket.SendAsync(
                        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(markObj)),
                        WebSocketMessageType.Text, true, ct);

                    continue;
                }

                if (update is ConversationInputTranscriptionFinishedUpdate transcriptionUpdate)
                {
                    Console.WriteLine($"User said: {transcriptionUpdate.Transcript}");
                    continue;
                }

                if (update is ConversationErrorUpdate errorUpdate)
                {
                    Console.WriteLine($"AI conversation error: {errorUpdate.Message}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in AI processing: {ex.Message}");

            // Optionally close the websocket on fatal errors
            //await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "AI processing error", CancellationToken.None);
        }
    }
}

