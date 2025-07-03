namespace VoiceCallAssistant.Models.Events;

record MediaEvent(BinaryData Audio, TimeSpan Elapsed) : TwilioEvent;