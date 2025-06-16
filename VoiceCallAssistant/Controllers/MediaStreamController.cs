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
    private readonly IConfiguration _configuration;

    //private readonly IRealTimeOpenAiService _aiService;

    public MediaStreamController(ITwilioService twilioService, IConfiguration configuration)
    {
        _twilioService = twilioService;
        _configuration = configuration;
        // _aiService = aiService;
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

        var realtimeClient = GetRealtimeConversationClient();
        var kernel = Kernel.CreateBuilder().Build();

        // Start a new conversation session.
        using RealtimeConversationSession session = await realtimeClient.StartConversationSessionAsync();

        // Initialize session options.
        // Session options control connection-wide behavior shared across all conversations,
        // including audio input format and voice activity detection settings.
        ConversationSessionOptions sessionOptions = new()
        {
            Voice = ConversationVoice.Ash,
            InputAudioFormat = ConversationAudioFormat.G711Ulaw,
            OutputAudioFormat = ConversationAudioFormat.G711Ulaw,
            Instructions = "Always start conversation with: I'm profile number 1845, welcome.",
        };

        // Configure session with defined options.
        await session.ConfigureSessionAsync(sessionOptions);

        // Kick off both loops
        var receiveTask = ReceiveFromTwilio(webSocket, cts.Token,
            sid => streamSid = sid,
            (audio, ts) => {
                latestTimestamp = ts;
                session.SendInputAudioAsync(audio, cts.Token).GetAwaiter().GetResult();
                Console.WriteLine($"Latest Timestamp: {ts}");
            });


        // Initialize dictionaries to store streamed audio responses and function arguments.
        Dictionary<string, MemoryStream> outputAudioStreamsById = [];

        // Output the size of received audio data and dispose streams.
        foreach ((string itemId, Stream outputAudioStream) in outputAudioStreamsById)
        {
            Console.WriteLine($"Raw audio output for {itemId}: {outputAudioStream.Length} bytes");

            outputAudioStream.Dispose();
        }

        
        var sendTask = SendToTwilio(session, cts.Token,
            async partDelta =>
            {
                // save audio bytes to stream by itemId
                if (!outputAudioStreamsById.TryGetValue(partDelta.ItemId, out MemoryStream? value))
                {
                    value = new MemoryStream();
                    outputAudioStreamsById[partDelta.ItemId] = value;
                }
                value.Write(partDelta.AudioBytes);

                // audio delta
                var delta = partDelta.AudioBytes;
                var payloadB64 = Convert.ToBase64String(delta);

                // once on first audio, capture start timestamp
                responseStartTs ??= latestTimestamp;
                if (partDelta.ItemId is not null)
                {
                    lastAssistantId = partDelta.ItemId;
                    contentPartsIndex = partDelta.ContentPartIndex;
                }                    

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
            async (speechStarted) =>
            {
                // on speech_started event
                if (markQueue.TryDequeue(out _) && responseStartTs.HasValue && lastAssistantId is not null)
                {
                    var elapsed = new TimeSpan(latestTimestamp - responseStartTs.Value);
                    await session.TruncateItemAsync(lastAssistantId, contentPartsIndex!.Value, elapsed, cts.Token);

                    // clear signal back to Twilio
                    var clearObj = new { @event = "clear", streamSid = streamSid };
                    await webSocket.SendAsync(
                        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(clearObj)),
                        WebSocketMessageType.Text, true, CancellationToken.None);

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
            Action<BinaryData, long> handleAudio)
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
                            .GetString();
                        var audioBytes = Convert.FromBase64String(payloadB64);
                        var audioBinary = new BinaryData(audioBytes);
                        var tsLong = Convert.ToInt64(ts);
                        handleAudio(audioBinary, tsLong);
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

    
    private async Task SendToTwilio(RealtimeConversationSession session,
        CancellationToken ct,
        Func<ConversationItemStreamingPartDeltaUpdate, Task> handleAudioDelta,
        Func<ConversationInputSpeechStartedUpdate, Task> handleSpeechStarted)
    {
        await foreach (ConversationUpdate update in session.ReceiveUpdatesAsync())
        {
            // Notification indicating the start of the conversation session.
            if (update is ConversationSessionStartedUpdate sessionStartedUpdate)
            {
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
        }
    }
    

    // OpenAI Realtime Implementation
    private RealtimeConversationClient GetRealtimeConversationClient()
    {
        var openAIOptions = _configuration.GetSection(OpenAIOptions.SectionName).Get<OpenAIOptions>();
        //var azureOpenAIOptions = _configuration.GetSection(AzureOpenAIOptions.SectionName).Get<AzureOpenAIOptions>();

        if (openAIOptions is not null && openAIOptions.IsValid)
        {
            return new RealtimeConversationClient(
                model: openAIOptions.Model,
                credential: new ApiKeyCredential(openAIOptions.ApiKey));
        }
        //else if (azureOpenAIOptions is not null && azureOpenAIOptions.IsValid)
        //{
        //    var client = new AzureOpenAIClient(
        //        endpoint: new Uri(azureOpenAIOptions.Endpoint),
        //        credential: new ApiKeyCredential(azureOpenAIOptions.ApiKey));

        //    return client.GetRealtimeConversationClient(azureOpenAIOptions.DeploymentName);
        //}
        else
        {
            throw new Exception("OpenAI/Azure OpenAI configuration was not found.");
        }
    }
}

