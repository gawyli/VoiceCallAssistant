using OpenAI.RealtimeConversation;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Models;
using VoiceCallAssistant.Models.Events;
using ILogger = Serilog.ILogger;

namespace VoiceCallAssistant.Services;

public class VoiceCallService : IVoiceCallService
{
        string systemMessage =
"""
You are wake-up call assistant. Your instructions are specified between markup <PersonalisedPrompt></PersonalisedPrompt>. 
Follow the instructions in the conversation you have with the user.

""";

    private readonly ILogger _logger;
    private readonly ITwilioService _twilioService;
    private readonly IRealtimeAIService _realtimeAIService;

    public VoiceCallService(ILogger logger, ITwilioService twilioService, IRealtimeAIService realtimeAIService)
    {
        _logger = logger;
        _twilioService = twilioService;
        _realtimeAIService = realtimeAIService;
    }

    public async Task<RealtimeConversationSession> CreateConversationSession(
        string userPrompt,
        CancellationTokenSource cancellationTokenSource)
    {
        var session = await _realtimeAIService.CreateConversationSessionAsync(cancellationTokenSource, systemMessage + userPrompt);
        _logger.Information("Conversation session started.");

        return session;
    }

    public async Task OrchestrateExchangeAsync(
        WebSocket webSocket1,
        RealtimeConversationSession webSocket2,
        CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            var state = new CallState();

            var incomingMessagesTask = ExchangeIncomingMessages(webSocket1, webSocket2, state, cancellationTokenSource.Token);
            var outgoingMessagesTask = ExchangeOutgoingMessages(webSocket1, webSocket2, state, cancellationTokenSource.Token);

            await Task.WhenAny(incomingMessagesTask, outgoingMessagesTask);
            await CloseWebSockets(webSocket1, webSocket2, cancellationTokenSource.Token);

            cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));   // Allow time for graceful shutdown

            await Task.WhenAll(incomingMessagesTask, outgoingMessagesTask); // Task.WhenAny does not await the other task.
            await CloseWebSockets(webSocket1, webSocket2, cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in WebSocket processing");
            await CloseWebSocketsWithError(webSocket1, webSocket2, "Internal server error occurred", CancellationToken.None);
            throw;
        }
    }

    private async Task ExchangeIncomingMessages(
        WebSocket webSocket,
        RealtimeConversationSession session,
        CallState state,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (TwilioEvent twilioEvent in _twilioService.ReceiveUpdatesAsync(webSocket, cancellationToken))
            {
                if (session.WebSocket.State != WebSocketState.Open || cancellationToken.IsCancellationRequested)
                {
                    _logger.Information("Cancellation requested, stopping incoming message processing.");
                    await _realtimeAIService.CloseRealtime(session, cancellationToken);

                    break;
                }
                if (twilioEvent is StartEvent startEvent)
                {
                    state.StreamSid = startEvent.StreamSid;
                    _logger.Information("Call started with SID: {CallSid}", state.StreamSid);
                    continue;
                }
                if (twilioEvent is MediaEvent mediaEvent)
                {
                    await session.SendInputAudioAsync(mediaEvent.Audio, cancellationToken);

                    state.StreamDurationTimestamp = mediaEvent.Elapsed;

                    continue;
                }
                if (twilioEvent is MarkEvent markEvent)
                {
                    state.MarkQueue.TryDequeue(out _);
                    continue;
                }
                if (twilioEvent is StopEvent stopEvent)
                {
                    await CloseWebSockets(webSocket, session, cancellationToken);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error while processing incoming messages.");
            throw new Exception("Error while processing incoming messages.", ex);
        }

    }

    private async Task ExchangeOutgoingMessages(
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
                    _logger.Information("Cancellation requested, stopping outgoing message processing.");
                    await _realtimeAIService.CloseRealtime(session, cancellationToken);
                    break;
                }

                if (update is ConversationSessionStartedUpdate sessionStartedUpdate)
                {
                    _logger.Information("AI session started: {SessionId}", sessionStartedUpdate.SessionId);
                    await session.StartResponseAsync(cancellationToken);

                    continue;
                }

                if (update is ConversationInputSpeechStartedUpdate speechStartedUpdate)
                {
                    //_logger.Debug("Speech detection started at {Time}", speechStartedUpdate.AudioStartTime);

                    if (state.MarkQueue.Any() && state.ResponseStartTs.HasValue && state.LastAssistantId != null)
                    {
                        var audioStartTime = speechStartedUpdate.AudioStartTime;
                        var elapsed = audioStartTime - state.ResponseStartTs.Value;

                        await session.TruncateItemAsync(
                            state.LastAssistantId.ToString()!,
                            (int)state.ContentPartsIndex!,
                            elapsed,
                            cancellationToken);

                        await _twilioService.ClearQueue(webSocket, state.StreamSid!, cancellationToken);

                        state.Clear();
                    }
                    continue;
                }

                if (update is ConversationInputSpeechFinishedUpdate speechFinishedUpdate)
                {
                    //_logger.Debug("Speech detection ended at {Time}", speechFinishedUpdate.AudioEndTime);
                    state.ResponseStartTs = speechFinishedUpdate.AudioEndTime;

                    continue;
                }

                if (update is ConversationItemStreamingPartDeltaUpdate deltaUpdate && deltaUpdate.AudioBytes != null)
                {
                    var delta = deltaUpdate.AudioBytes;
                    var payloadB64 = Convert.ToBase64String(delta);

                    state.ResponseStartTs ??= state.StreamDurationTimestamp;    // ResponseStartTs is null here only for the first response.
                    state.LastAssistantId = deltaUpdate.ItemId;
                    state.ContentPartsIndex = deltaUpdate.ContentPartIndex;

                    await _twilioService.SendInputAudioAsync(
                        webSocket,
                        payloadB64,
                        state,
                        cancellationToken);

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
                    throw new Exception($"AI conversation error: {errorUpdate.Message}");
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
        await _twilioService.CloseTwilioWithError(webSocket, message, cancellationToken);
        await _realtimeAIService.CloseRealtimeWithError(session, message, cancellationToken);
    }

    private async Task CloseWebSockets(WebSocket webSocket,
        RealtimeConversationSession session,
        CancellationToken cancellationToken)
    {
        await _twilioService.CloseTwilio(webSocket, cancellationToken);
        await _realtimeAIService.CloseRealtime(session, cancellationToken);
    }
}

