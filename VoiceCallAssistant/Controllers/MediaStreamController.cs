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
using ILogger = Serilog.ILogger;

namespace VoiceCallAssistant.Controllers;

[ApiController]
[Route("/ws")]
public class MediaStreamController : ControllerBase
{
    private readonly ILogger _logger;
    private readonly IRepository _repository;
    private readonly ITwilioService _twilioService;
    private readonly IRealtimeAIService _realtimeAiService;
    private readonly IConfiguration _configuration;

    public MediaStreamController(ILogger logger, IRepository repository, ITwilioService twilioService, IRealtimeAIService realtimeAiService, IConfiguration configuration)
    {
        _logger = logger;
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
            _logger.Warning("Rejected non-WebSocket request to media-stream endpoint.");
            this.HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
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
            _logger.Warning("Routine ID was not provided or invalid in WebSocket request path.");
            this.HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        _logger.Information("WebSocket request received for Routine ID: {RoutineId}", routineId);

        string systemMessage =
        """
        You are wake-up call assistant. Your instructions are specified between markup <PersonalisedPrompt></PersonalisedPrompt>. 
        Follow the instructions in the conversation you have with the user.

        """;

        var routine = await _repository.GetByIdAsync<Routine>(routineId, this.HttpContext.RequestAborted);
        if (routine == null)
        {
            _logger.Warning("Routine not found for Routine ID: {RoutineId}", routineId);
            return;
        }

        _logger.Information("Routine loaded for Routine ID: {RoutineId}", routineId);

        // TODO: Add Interests and Tasks
        systemMessage += $"<PersonalisedPrompt> {routine.Preferences.PersonalisedPrompt} </PersonalisedPrompt>";

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

            _logger.Information("Conversation session started.");

            await ProcessWebSocketConnection(webSocket, session, state, cts.Token);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing WebSocket stream.");
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
            _logger.Warning("Closing WebSocket with error: {Message}", message);
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
                    _logger.Information("WebSocket closed by client.");
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
                _logger.Error(ex, "WebSocket error while receiving.");
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
                _logger.Debug("Received WebSocket event: {EventType}", eventType);
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
                _logger.Information("Stream stop event received.");
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

        _logger.Information("Stream started with SID: {StreamSid}", state.StreamSid);
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
                    _logger.Information("AI session started: {SessionId}", sessionStartedUpdate.SessionId);
                    await session.StartResponseAsync();                    
                    continue;
                }

                // Process incoming speech detection (used for barge-in)
                if (update is ConversationInputSpeechStartedUpdate speechStartedUpdate)
                {
                    _logger.Debug("Speech detection started at {Time}", speechStartedUpdate.AudioStartTime);

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
                    _logger.Debug("Speech detection ended at {Time}", speechFinishedUpdate.AudioEndTime);
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

                    state.MarkQueue.Enqueue(state.LastAssistantId);
                    var markObj = new
                    {
                        @event = "mark",
                        streamSid = state.StreamSid,
                        mark = new { name = state.LastAssistantId }
                    };
                    await webSocket.SendAsync(
                        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(markObj)),
                        WebSocketMessageType.Text, true, ct);

                    continue;
                }

                //if (update is ConversationInputTranscriptionFinishedUpdate transcriptionUpdate)
                //{
                //    _logger.Debug("User said: {Transcript}", transcriptionUpdate.Transcript);
                //    continue;
                //}

                if (update is ConversationErrorUpdate errorUpdate)
                {
                    _logger.Error("AI conversation error: {ErrorMessage}", errorUpdate.Message);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in AI processing");

            // Optionally close the websocket on fatal errors
            //await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "AI processing error", CancellationToken.None);
        }
    }
}

