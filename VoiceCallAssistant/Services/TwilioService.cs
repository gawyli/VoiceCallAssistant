using Twilio;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;
using Twilio.TwiML.Voice;
using VoiceCallAssistant.Interfaces;

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
        var twilioConfig = configuration.GetRequiredSection("Twilio").Get<TwilioServiceConfig>();
        if (twilioConfig == null)
        {
            throw new ArgumentNullException(nameof(twilioConfig), "Twilio configuration is not set in appsettings.json.");
        }

        _accountSid = twilioConfig.AccountSid;
        _authToken = twilioConfig.AuthToken;
        _callerId = twilioConfig.CallerId;
        _webhookHost = $"https://{twilioConfig.WebhookHost}/RequestOutboundCallWebhook";
    }

    public void CreateClient()
    {
        TwilioClient.Init(_accountSid, _authToken);
    }

    public string MakeCall(string toPhoneNumber)
    {
        var to = new Twilio.Types.PhoneNumber(toPhoneNumber);
        var from = new Twilio.Types.PhoneNumber(_callerId);
        
        var callOptions = new CreateCallOptions(to, from)
        {
            Url = new Uri($"https://{_webhookHost}/RequestOutboundCallWebhook"),
            StatusCallback = new Uri($"https://{_webhookHost}/RequestOutboundCallWebhook"),
            StatusCallbackEvent = new List<string> { "initiated", "ringing", "answered", "completed" }
        };

        var call = CallResource.Create(callOptions);
        
        Console.WriteLine($"Call initiated with SID: {call.Sid}");
        return call.Sid;
    }

    public string ConnectWebhook(string toPhoneNumber)
    {
        var response = new Twilio.TwiML.VoiceResponse();
        response.Say("Hello, this is a test call from Voice Call Assistant. Please hold while we connect you to the user.");
        response.Pause(1);

        var connect = new Connect();

        var stream = connect.Stream(url: $"wss://{_webhookHost}/media-stream");
        stream.SetOption("phone_number", toPhoneNumber);

        response.Append(connect);

        Console.WriteLine($"Returning TwiML for the outbound call");
        return response.ToString();
    }
}
