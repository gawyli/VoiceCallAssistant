namespace VoiceCallAssistant.Interfaces;

public interface ITwilioService
{
    public void CreateClient();
    public string MakeCall(string toPhoneNumber);
    public string ConnectWebhook();
}
