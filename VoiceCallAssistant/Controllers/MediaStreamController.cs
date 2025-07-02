using Microsoft.AspNetCore.Mvc;
using OpenAI.RealtimeConversation;
using System.Collections.Concurrent;
using System.Net.WebSockets;
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
    public async Task MediaStreamWebsocketGet(CancellationToken cancellationToken)
    {
        try
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

            var routine = await _repository.GetByIdAsync<Routine>(routineId, cancellationToken);
            if (routine == null)
            {
                _logger.Warning("Routine not found for Routine ID: {RoutineId}", routineId);
                return;
            }

            _logger.Information("Routine loaded for Routine ID: {RoutineId}", routineId);

            // TODO: Add Interests and Tasks
            systemMessage += $"<PersonalisedPrompt> {routine.Preferences.PersonalisedPrompt} </PersonalisedPrompt>";

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var session = await _realtimeAiService.CreateConversationSessionAsync(linkedCts, systemMessage);
            _logger.Information("Conversation session started.");

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            _logger.Information("WebSocket connection established.");


            await HandleMediaStream(webSocket, session, linkedCts);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error Handling Media Stream.");
            this.HttpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }
    }

    private async Task HandleMediaStream(WebSocket webSocket, RealtimeConversationSession session, CancellationTokenSource cancellationTokenSource)
    {
        var state = new CallState
        {
            StreamSid = null,
            StreamDurationTimestamp = new TimeSpan(),
            LastAssistantId = null,
            ContentPartsIndex = null,
            ResponseStartTs = null,
            MarkQueue = new ConcurrentQueue<string>()
        };


        await ProcessWebSocketsConnection(webSocket, session, state, cancellationTokenSource);
    }

    private async Task ProcessWebSocketsConnection(
        WebSocket webSocket,
        RealtimeConversationSession session,
        CallState state,
        CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            var twilioToRealtimeTask = ProcessIncomingMessages(webSocket, session, state, cancellationTokenSource.Token);
            var realtimeToTwilioTask = ProcessOutgoingMessages(webSocket, session, state, cancellationTokenSource.Token);

            var finished = await Task.WhenAny(twilioToRealtimeTask, realtimeToTwilioTask);
            _logger.Information("WebSocket processing finished with task: {FinishedTask}", finished.Id);

            await CloseWebSockets(webSocket, session, cancellationTokenSource.Token);

            cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));   // Allow time for graceful shutdown

            await Task.WhenAll(twilioToRealtimeTask, realtimeToTwilioTask); // Task.WhenAny does not await the other task.
            await CloseWebSockets(webSocket, session, cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            var message = "An error occurred while processing the WebSocket connection.";
            _logger.Error(ex, $"{message}");
            await CloseWebSocketsWithError(webSocket, session, $"{message}", CancellationToken.None);
            throw new Exception($"{message}", ex);
        }

    }

    private async Task ProcessIncomingMessages(
        WebSocket webSocket,
        RealtimeConversationSession session,
        CallState state,
        CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new byte[4 * 1024];

            while (webSocket.State == WebSocketState.Open &&
                session.WebSocket.State == WebSocketState.Open &&
                !cancellationToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.Information("WebSocket closed by client.");
                    await session.WebSocket.CloseAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription,
                        cancellationToken);

                    break;
                }

                await ProcessIncomingMessage(buffer, result.Count, webSocket, session, state, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error while processing incoming messages.");
            throw new Exception("Error while processing incoming messages.", ex);
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
                _logger.Debug("Stream stop event received.");
                await CloseTwilio(webSocket, ct);
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
        var timestamp = Convert.ToDouble(timestampStr);

        state.StreamDurationTimestamp = TimeSpan.FromMilliseconds(timestamp);

        return audioBinary;
    }

    private async Task ProcessOutgoingMessages(
        WebSocket webSocket,
        RealtimeConversationSession session,
        CallState state,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ConversationUpdate update in session.ReceiveUpdatesAsync(cancellationToken))
            {
                if (webSocket.State != WebSocketState.Open || cancellationToken.IsCancellationRequested)
                {
                    await CloseRealtime(session, cancellationToken);
                    break;
                }

                if (update is ConversationSessionStartedUpdate sessionStartedUpdate)
                {
                    _logger.Information("AI session started: {SessionId}", sessionStartedUpdate.SessionId);
                    await session.StartResponseAsync(cancellationToken);                    

                    continue;
                }

                // Process incoming speech detection (used for barge-in)
                if (update is ConversationInputSpeechStartedUpdate speechStartedUpdate)
                {
                    _logger.Debug("Speech detection started at {Time}", speechStartedUpdate.AudioStartTime);

                    if (state.MarkQueue.Any() && state.ResponseStartTs.HasValue && state.LastAssistantId != null)
                    {
                        var audioStartTime = speechStartedUpdate.AudioStartTime;
                        var elapsed = audioStartTime - state.ResponseStartTs.Value;
                        
                        await session.TruncateItemAsync(
                            state.LastAssistantId.ToString()!,
                            (int)state.ContentPartsIndex!,
                            elapsed,
                            cancellationToken);

                        if (webSocket.State == WebSocketState.Open)
                        {
                            var clearObj = new { @event = "clear", streamSid = state.StreamSid };
                            await webSocket.SendAsync(
                                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(clearObj)),
                                WebSocketMessageType.Text,
                                true,
                                cancellationToken);
                        }

                        state.Clear();
                    }
                    continue;
                }

                if (update is ConversationInputSpeechFinishedUpdate speechFinishedUpdate)
                {
                    _logger.Debug("Speech detection ended at {Time}", speechFinishedUpdate.AudioEndTime);
                    state.ResponseStartTs = speechFinishedUpdate.AudioEndTime;

                    continue;
                }

                if (update is ConversationItemStreamingPartDeltaUpdate deltaUpdate && deltaUpdate.AudioBytes != null)
                {
                    var delta = deltaUpdate.AudioBytes;
                    var payloadB64 = Convert.ToBase64String(delta);

                    state.ResponseStartTs ??= state.StreamDurationTimestamp;

                    state.LastAssistantId = deltaUpdate.ItemId;
                    state.ContentPartsIndex = deltaUpdate.ContentPartIndex;

                    if (webSocket.State == WebSocketState.Open)
                    {
                        var mediaObj = new
                        {
                            @event = "media",
                            streamSid = state.StreamSid,
                            media = new { payload = payloadB64 }
                        };
                        await webSocket.SendAsync(
                        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mediaObj)),
                        WebSocketMessageType.Text, true, cancellationToken);

                        state.MarkQueue.Enqueue(state.LastAssistantId);
                        var markObj = new
                        {
                            @event = "mark",
                            streamSid = state.StreamSid,
                            mark = new { name = state.LastAssistantId }
                        };
                        await webSocket.SendAsync(
                            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(markObj)),
                            WebSocketMessageType.Text, true, cancellationToken);
                    }

                    continue;
                }

                //if (update is ConversationInputTranscriptionFinishedUpdate transcriptionUpdate)
                //{
                //    _logger.Debug("User said: {Transcript}", transcriptionUpdate.Transcript);
                //    continue;
                //}

                if (update is ConversationErrorUpdate errorUpdate)
                {
                    _logger.Error("Error while processig conversation: {ErrorMessage}", errorUpdate.Message);                    
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error while processing outgoing messages.");
            throw new Exception("Error while processing outgoing messages.", ex);
        }
    }

    private async Task CloseWebSocketsWithError(WebSocket webSocket,
        RealtimeConversationSession session,
        string message,
        CancellationToken cancellationToken)
    {
        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            _logger.Warning("Closing WebSocket with error: {Message}", message);
            await webSocket.CloseAsync(
                    WebSocketCloseStatus.InternalServerError,
                    message,
                    cancellationToken);
        }
        if (session.WebSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            _logger.Warning("Closing WebSocket with error: {Message}", message);
            await session.WebSocket.CloseAsync(
                    WebSocketCloseStatus.InternalServerError,
                    message,
                    cancellationToken);
        }
    }

    private async Task CloseWebSockets(WebSocket webSocket,
        RealtimeConversationSession session,
        CancellationToken cancellationToken)
    {
        await CloseTwilio(webSocket, cancellationToken);
        await CloseRealtime(session, cancellationToken);
    }

    private async Task CloseTwilio(WebSocket webSocket,
        CancellationToken cancellationToken)
    {
        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            _logger.Debug("Closing Twilio WebSocket connection.");
            await webSocket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure,
                "NormalClosure",
                cancellationToken);
        }
    }

    private async Task CloseRealtime(RealtimeConversationSession session,
        CancellationToken cancellationToken)
    {
        if (session.WebSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            _logger.Debug("Closing Realtime Conversation Session WebSocket connection.");
            await session.WebSocket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure,
                "NormalClosure",
                cancellationToken);
        }
    }

}