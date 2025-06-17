namespace VoiceCallAssistant.Messages.TwilioMessages;

public class MediaPayloadObject : BaseEvent
{
    public string StreamSid { get; set; } = null!;
    public MediaPayload Media { get; set; } = null!;
}

public class MediaPayload
{
    public string Payload { get; set; } = null!;
}