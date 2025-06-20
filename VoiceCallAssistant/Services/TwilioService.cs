using OpenAI.RealtimeConversation;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.TwiML;
using Twilio.TwiML.Voice;
using Twilio.Security;
using VoiceCallAssistant.Interfaces;
using Task = System.Threading.Tasks.Task;
using Microsoft.AspNetCore.Http.Extensions;
using System.Security.Policy;

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
        _timeCallLimit = twilioConfig.TimeCallLimit;
    }

    public void CreateClient()
    {
        TwilioClient.Init(_accountSid, _authToken);
        Console.WriteLine("Client Created");
    }

    public bool ValidateRequest(HttpRequest request)
    {
        // TODO: validate websocket
        var url = request.GetDisplayUrl();
        var signature = request.Headers["X-Twilio-Signature"].ToString();
        var body = request.Form.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());
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
}
