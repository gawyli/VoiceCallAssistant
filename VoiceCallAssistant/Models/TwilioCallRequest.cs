using System.Text.Json.Serialization;

namespace VoiceCallAssistant.Models;

public class TwilioCallRequest
{
    [JsonPropertyName("CallSid")]
    public string CallSid { get; set; } = default!;

    [JsonPropertyName("AccountSid")]
    public string AccountSid { get; set; } = default!;

    [JsonPropertyName("From")]
    public string From { get; set; } = default!;

    [JsonPropertyName("To")]
    public string To { get; set; } = default!;

    [JsonPropertyName("CallStatus")]
    public string CallStatus { get; set; } = default!;

    [JsonPropertyName("ApiVersion")]
    public string ApiVersion { get; set; } = default!;

    [JsonPropertyName("Direction")]
    public string Direction { get; set; } = default!;

    [JsonPropertyName("ForwardedFrom")]
    public string? ForwardedFrom { get; set; }

    [JsonPropertyName("CallerName")]
    public string? CallerName { get; set; }

    [JsonPropertyName("ParentCallSid")]
    public string? ParentCallSid { get; set; }

    [JsonPropertyName("CallToken")]
    public string? CallToken { get; set; }
}
