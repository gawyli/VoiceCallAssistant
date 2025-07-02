using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.TwiML;
using Twilio.TwiML.Voice;
using Twilio.Security;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Models;
using VoiceCallAssistant.Models.Events;
using ILogger = Serilog.ILogger;
using Task = System.Threading.Tasks.Task;


namespace VoiceCallAssistant.Services;

public class TwilioServiceConfig
{
    public string AccountSid { get; set; } = null!;
    public string AuthToken { get; set; } = null!;
    public string CallerId { get; set; } = null!;
    public string WebhookHost { get; set; } = null!;
    public int TimeCallLimit { get; set; }

}

public class TwilioService : ITwilioService
{
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly string _callerId;
    private readonly string _webhookHost;
    private readonly int _timeCallLimit;
    private readonly ILogger _logger;

    public TwilioService(ILogger logger, IConfiguration configuration)
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
        _timeCallLimit = twilioConfig.TimeCallLimit;
        _logger = logger;
    }

    public void CreateClient()
    {
        TwilioClient.Init(_accountSid, _authToken);
        Console.WriteLine("Client Created");
    }

    public bool ValidateRequest(string url, IHeaderDictionary headers, IFormCollection form)
    {
        // TODO: validate websocket
        var signature = headers["X-Twilio-Signature"].ToString();
        var body = form.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());
        //var parameters = request.QueryString.ToString();

        try
        {
            var validator = new RequestValidator(_authToken);
            var isValid = validator.Validate(url, body, signature);
            if (!isValid)
            {
                Console.WriteLine("Invalid request signature.");
                return false;
            }
            Console.WriteLine($"Request validation result: {isValid}");
            return isValid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error validating request: {ex.Message}");
            return false;
        }
    }

    public string MakeCall(string toPhoneNumber, string routineId)
    {
        var to = new Twilio.Types.PhoneNumber(toPhoneNumber);
        var from = new Twilio.Types.PhoneNumber(_callerId);

        var callOptions = new CreateCallOptions(to, from)
        {
            Url = new Uri($"https://{_webhookHost}/api/call/webhook/{routineId}"),
            StatusCallback = new Uri($"https://{_webhookHost}/api/call/webhook/{routineId}"),
            StatusCallbackEvent = new List<string> { "initiated", "completed" },
            StatusCallbackMethod = Twilio.Http.HttpMethod.Post,
            TimeLimit = _timeCallLimit
        };

        var call = CallResource.Create(callOptions);

        Console.WriteLine($"Call initiated with SID: {call.Sid}");
        return call.Sid;
    }

    public string ConnectWebhook(string routineId)
    {
        Console.WriteLine("Connecting webhook");

        var response = new VoiceResponse();
        response.Say("Connecting..");

        var connect = new Connect();
        var stream = new Twilio.TwiML.Voice.Stream(url: $"wss://{_webhookHost}/ws/media-stream/{routineId}");
        //stream.Parameter(name: "Auth", value: "Token");

        connect.Append(stream);
        response.Append(connect);

        Console.WriteLine($"Returning TwiML for the outbound call");
        return response.ToString();
    }

    public async Task ClearQueue(WebSocket webSocket, string streamSid, CancellationToken cancellationToken)
    {
        if (webSocket.State != WebSocketState.Open)
        {
            _logger.Error("WebSocket is not open. Cannot send audio.");
            throw new InvalidOperationException("WebSocket is not open.");
        }

        var clearObj = new { @event = "clear", streamSid = streamSid };
        await webSocket.SendAsync(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(clearObj)),
            WebSocketMessageType.Text,
            true,
            cancellationToken);
    }

    public async Task SendInputAudioAsync(WebSocket webSocket, string payloadB64, CallState state, CancellationToken cancellationToken)
    {
        if (webSocket.State != WebSocketState.Open)
        {
            _logger.Error("WebSocket is not open. Cannot send audio.");
            throw new InvalidOperationException("WebSocket is not open.");
        }

        var mediaObj = new
        {
            @event = "media",
            streamSid = state.StreamSid,
            media = new { payload = payloadB64 }
        };
        await webSocket.SendAsync(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mediaObj)),
            WebSocketMessageType.Text, true, cancellationToken);

        state.MarkQueue.Enqueue(state.LastAssistantId!);

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

    public async IAsyncEnumerable<TwilioEvent> ReceiveUpdatesAsync(WebSocket webSocket, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[4 * 1024];  

        while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.Information("WebSocket closed by client.");
                await CloseTwilio(webSocket, cancellationToken);
            }

            using var memoryStream = new MemoryStream(buffer, 0, result.Count);

            yield return await ReceiveUpdateAsync(memoryStream, cancellationToken);
        }
    }

    private async Task<TwilioEvent> ReceiveUpdateAsync(
    MemoryStream stream,
    CancellationToken cancellationToken)
    {
        var doc = await JsonDocument.ParseAsync(stream, default, cancellationToken);
        var root = doc.RootElement;

        root.TryGetProperty("event", out var eventProp);
        var eventType = eventProp.GetString();

        switch (eventType)
        {
            case "start":
                _logger.Debug("Stream start event received");
                return HandleStartEvent(root);

            case "media":
                return HandleMediaEvent(root);

            case "mark":
                return new MarkEvent(eventType);

            case "stop":
                _logger.Debug("Stream stop event received.");
                return new StopEvent();

            default:
                return new ConnectedEvent();                
        }
    }

    private StartEvent HandleStartEvent(
        JsonElement root)
    {
        var streamSid = root
                .GetProperty("start")
                .GetProperty("streamSid")
                .GetString()!;

        _logger.Information("Stream started with SID: {StreamSid}", streamSid);

        return new StartEvent(streamSid);
    }

    private MediaEvent HandleMediaEvent(
        JsonElement root)
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

        return new MediaEvent(audioBinary, TimeSpan.FromMilliseconds(timestamp));
    }

    public async Task CloseTwilio(WebSocket webSocket,
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
    public async Task CloseTwilioWithError(WebSocket webSocket, string message,
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
    }
}
