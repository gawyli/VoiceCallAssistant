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

    [HttpGet("media-stream", Name = "MediaStreamWebsocket")]
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

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await HandleMediaStreamNew(webSocket);
    }

    private async Task HandleMediaStreamNew(WebSocket webSocket)
    {
        using var cts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, HttpContext.RequestAborted);

        // Set reasonable timeout
        cts.CancelAfter(TimeSpan.FromMinutes(30));

        try
        {
            // State tracking
            var state = new CallState
            {
                StreamSid = null,
                LatestTimestamp = 0,
                LastAssistantId = null,
                ContentPartsIndex = null,
                ResponseStartTs = null,
                MarkQueue = new ConcurrentQueue<string>()
            };

            // Initialize AI conversation
            string systemMessage = "You are wake up call asistant. Welcome your user as his specified between markup <PresonlalisedPrompt></PresonlalisedPrompt>";
            using var session = await _realtimeAiService.CreateConversationSessionAsync(cts, systemMessage);

            // Start processing
            await ProcessWebSocketConnection(webSocket, session, state, linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation - ignore
        }
        catch (Exception ex)
        {
            // Log exception
            Console.WriteLine($"Error processing WebSocket: {ex.InnerException}");

            // Close socket if still open
            if (webSocket.State == WebSocketState.Open)
            {
                await CloseWebSocketWithError(webSocket, "Internal server error occurred");
            }
        }
        finally
        {
            cts.Cancel();
        }
    }

    private async Task CloseWebSocketWithError(WebSocket webSocket, string message)
    {
        await webSocket.CloseAsync(
                WebSocketCloseStatus.InternalServerError,
                message,
                CancellationToken.None);
    }

    private async Task ProcessWebSocketConnection(
        WebSocket webSocket,
        RealtimeConversationSession session, 
        CallState state, 
        CancellationToken ct)
    {
        // Start both message loops
        var receiveTask = ProcessIncomingMessages(webSocket, session, state, ct);
        var sendTask = ProcessOutgoingMessages(webSocket, session, state, ct);

        // Wait for both to complete
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

                // Process the message
                await ProcessIncomingMessage(buffer, result.Count, session, state, ct);
            }
            catch (WebSocketException ex)
            {
                // Handle WebSocket errors
                Console.WriteLine($"WebSocket error occurred: {ex.InnerException}");
                break;
            }
        }
    }

    private async Task ProcessIncomingMessage(
        byte[] buffer, 
        int count,
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
                await HandleStartEvent(root, session, state, ct);
                break;

            case "media":
                await HandleMediaEvent(root, session, state, ct);
                break;

            case "mark":
                state.MarkQueue.TryDequeue(out _);
                break;

            case "stop":
                // Handle stop event
                break;
        }
    }

    private async Task HandleStartEvent(
        JsonElement root, 
        RealtimeConversationSession session, 
        CallState state,
        CancellationToken ct)
    {
        try
        {
            // Extract streamSid from the start event
            state.StreamSid = root
                .GetProperty("start")
                .GetProperty("streamSid")
                .GetString()!;

            // Extract phone number if available in custom parameters
            if (root.GetProperty("start").TryGetProperty("customParameters", out var customParams))
            {
                if (customParams.TryGetProperty("phone-number", out var phoneNumberProp))
                {
                    string? phoneNumber = phoneNumberProp.GetString();
                    if (!string.IsNullOrEmpty(phoneNumber))
                    {
                        //var routine = await _repository.GetBySpec(phoneNumber);
                        //await session .AddItemAsync(
                        //    ConversationItem.CreateSystemMessage(
                        //        [$"{routine.PersonalisedPrompt}"]), ct);

                        // You can store or log the phone number here if needed
                        Console.WriteLine($"Call connected with phone number: {phoneNumber}");
                    }
                }
            }

            Console.WriteLine($"Stream started with SID: {state.StreamSid}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing start event: {ex.Message}");
        }
    }

    private async Task HandleMediaEvent(
        JsonElement root, 
        RealtimeConversationSession session, 
        CallState state, 
        CancellationToken ct)
    {
        try
        {
            // Extract audio payload
            var payloadB64 = root
                .GetProperty("media")
                .GetProperty("payload")
                .GetString();

            // Extract timestamp
            var timestampStr = root
                .GetProperty("media")
                .GetProperty("timestamp")
                .GetString();

            if (string.IsNullOrEmpty(payloadB64) || string.IsNullOrEmpty(timestampStr))
                return;

            // Convert to binary data
            var audioBytes = Convert.FromBase64String(payloadB64);
            var audioBinary = new BinaryData(audioBytes);
            var timestamp = Convert.ToInt64(timestampStr);

            // Update the timestamp for barge-in calculations
            state.LatestTimestamp = timestamp;

            // Send the audio to the AI session
            await session.SendInputAudioAsync(audioBinary, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing media event: {ex.Message}");
        }
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

                    // Handle barge-in logic
                    if (state.MarkQueue.Any() && state.ResponseStartTs.HasValue && state.LastAssistantId != null)
                    {
                        var elapsed = new TimeSpan(state.LatestTimestamp - (long)state.ResponseStartTs);
                        await session.TruncateItemAsync(
                            state.LastAssistantId.ToString()!,
                            (int)state.ContentPartsIndex!,
                            elapsed,
                            ct);

                        // Send clear signal to Twilio to stop audio
                        var clearObj = new { @event = "clear", streamSid = state.StreamSid };
                        await webSocket.SendAsync(
                            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(clearObj)),
                            WebSocketMessageType.Text,
                            true,
                            ct);

                        // Reset state for next response
                        state.MarkQueue.Clear();
                        state.ResponseStartTs = null;
                        state.LastAssistantId = null;
                        state.ContentPartsIndex = null;
                    }
                    continue;
                }

                // Process speech end events if needed
                if (update is ConversationInputSpeechFinishedUpdate speechFinishedUpdate)
                {
                    Console.WriteLine($"Speech detection ended at {speechFinishedUpdate.AudioEndTime}");
                    continue;
                }

                // Process AI-generated audio response
                if (update is ConversationItemStreamingPartDeltaUpdate deltaUpdate && deltaUpdate.AudioBytes != null)
                {
                    // Convert audio bytes to base64 for transmission
                    var delta = deltaUpdate.AudioBytes;
                    var payloadB64 = Convert.ToBase64String(delta);

                    // On first audio chunk, capture start timestamp for barge-in timing
                    state.ResponseStartTs ??= state.LatestTimestamp;

                    // Store AI response tracking info for possible truncation
                    state.LastAssistantId = deltaUpdate.ItemId;
                    state.ContentPartsIndex = deltaUpdate.ContentPartIndex;

                    // Send media event with audio payload
                    var mediaObj = new
                    {
                        @event = "media",
                        streamSid = state.StreamSid,
                        media = new { payload = payloadB64 }
                    };
                    await webSocket.SendAsync(
                        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mediaObj)),
                        WebSocketMessageType.Text, true, ct);

                    // Send mark to track response parts for barge-in
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

                // Process transcription completion
                if (update is ConversationInputTranscriptionFinishedUpdate transcriptionUpdate)
                {
                    Console.WriteLine($"User said: {transcriptionUpdate.Transcript}");
                    continue;
                }

                // Handle errors
                if (update is ConversationErrorUpdate errorUpdate)
                {
                    Console.WriteLine($"AI conversation error: {errorUpdate.Message}");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in AI processing: {ex.Message}");
            // Optionally close the websocket on fatal errors
            // await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "AI processing error", CancellationToken.None);
        }
    }

    //private async Task HandleMediaStream(WebSocket webSocket)
    //{
    //    var phoneNumber = this.HttpContext.Request.Query["phone-number"].ToString();

    //    var cts = new CancellationTokenSource();
    //    var markQueue = new ConcurrentQueue<string>();

    //    string? streamSid = null;
    //    long latestTimestamp = 0;
    //    string? lastAssistantId = null;
    //    int? contentPartsIndex = null;
    //    long? responseStartTs = null;

    //    // Get a prompt from the Routine Object from Db
    //    //string systemMessage = "Welcome a user with your profile number.";
    //    using var session = await _realtimeAiService.CreateConversationSessionAsync(cts);

    //    // Kick off both loops
    //    var receiveTask = _twilioService.ReceiveFrom(webSocket, cts.Token,
    //        sid => streamSid = sid,
    //        async (audio, ts) =>
    //        {
    //            latestTimestamp = ts;
    //            await session.SendInputAudioAsync(audio, cts.Token);
    //        },
    //        markQueue);
        
    //    var sendTask = _twilioService.SendTo(session, cts.Token,
    //        async partDelta =>
    //        {
    //            // audio delta
    //            var delta = partDelta.AudioBytes;
    //            var payloadB64 = Convert.ToBase64String(delta);

    //            // once on first audio, capture start timestamp
    //            responseStartTs ??= latestTimestamp;

    //            lastAssistantId = partDelta.ItemId;
    //            contentPartsIndex = partDelta.ContentPartIndex;                   

    //            // send media event
    //            var mediaObj = new
    //            {
    //                @event = "media",
    //                streamSid = streamSid,
    //                media = new { payload = payloadB64 }
    //            };
    //            await webSocket.SendAsync(
    //                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mediaObj)),
    //                WebSocketMessageType.Text, true, cts.Token);

    //            // send mark
    //            markQueue.Enqueue("responsePart");
    //            var markObj = new
    //            {
    //                @event = "mark",
    //                streamSid = streamSid,
    //                mark = new { name = "responsePart" }
    //            };
    //            await webSocket.SendAsync(
    //                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(markObj)),
    //                WebSocketMessageType.Text, true, cts.Token);
    //        },
    //        async (speechStarted) =>
    //        {
    //            // on speech_started event
    //            if (markQueue.Any() && responseStartTs.HasValue && lastAssistantId is not null)
    //            {
    //                var elapsed = new TimeSpan(latestTimestamp - responseStartTs.Value);
    //                await session.TruncateItemAsync(lastAssistantId, contentPartsIndex!.Value, elapsed, cts.Token);

    //                // clear signal back to Twilio
    //                var clearObj = new { @event = "clear", streamSid = streamSid };
    //                await webSocket.SendAsync(
    //                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(clearObj)),
    //                    WebSocketMessageType.Text, true, cts.Token);

    //                markQueue.Clear();
    //                responseStartTs = null;
    //                lastAssistantId = null;
    //                contentPartsIndex = null;
    //            }
    //        });

    //    await Task.WhenAll(receiveTask, sendTask);
    //    cts.Cancel();
    //}
}

