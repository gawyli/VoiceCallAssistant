namespace VoiceCallAssistant.Models.Events;

record StartEvent(string StreamSid) : TwilioEvent;