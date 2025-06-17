using OpenAI.RealtimeConversation;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.TwiML;
using Twilio.TwiML.Voice;
using VoiceCallAssistant.Interfaces;
using Task = System.Threading.Tasks.Task;

namespace VoiceCallAssistant.Services;

public class TwilioServiceConfig
{
    public string AccountSid { get; set; } = null!;
    public string AuthToken { get; set; } = null!;
    public string CallerId { get; set; } = null!;
    public string WebhookHost { get; set; } = null!;

}

public class TwilioService : ITwilioService
{
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly string _callerId;
    private readonly string _webhookHost;

    public TwilioService(IConfiguration configuration)
    {
        var twilioConfig = configuration.GetRequiredSection("TwilioService").Get<TwilioServiceConfig>();
        if (twilioConfig == null)
        {
            throw new ArgumentNullException(nameof(twilioConfig), "Twilio configuration is not set in appsettings.json.");
        }

        _accountSid = twilioConfig.AccountSid;
        _authToken = twilioConfig.AuthToken;
        _callerId = twilioConfig.CallerId;
        _webhookHost = twilioConfig.WebhookHost;
    }

    public void CreateClient()
    {
        TwilioClient.Init(_accountSid, _authToken);
        Console.WriteLine("Client Created");
    }

    public string MakeCall(string toPhoneNumber)
    {
        var to = new Twilio.Types.PhoneNumber(toPhoneNumber);
        var from = new Twilio.Types.PhoneNumber(_callerId);

        var callOptions = new CreateCallOptions(to, from)
        {
            Url = new Uri($"https://{_webhookHost}/api/call/webhook"),
            StatusCallback = new Uri($"https://{_webhookHost}/api/call/webhook"),
            StatusCallbackEvent = new List<string> { "initiated", "ringing", "answered", "completed" }
        };

        var call = CallResource.Create(callOptions);

        Console.WriteLine($"Call initiated with SID: {call.Sid}");
        return call.Sid;
    }

    public string ConnectWebhook()
    {
        Console.WriteLine("Connecting webhook");

        var response = new VoiceResponse();
        response.Say("Connecting..");

        var connect = new Connect();
        connect.Stream(url: $"wss://{_webhookHost}/ws/media-stream");

        response.Append(connect);

        Console.WriteLine($"Returning TwiML for the outbound call");
        return response.ToString();
    }

    public async Task ReceiveFrom(
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

    public async Task SendTo(RealtimeConversationSession session,
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
