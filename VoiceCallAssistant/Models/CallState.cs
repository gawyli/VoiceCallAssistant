using System.Collections.Concurrent;

namespace VoiceCallAssistant.Models;
internal class CallState
{
    public string? StreamSid { get; set; }
    public long LatestTimestamp { get; set; }
    public string? LastAssistantId { get; set; }
    public int? ContentPartsIndex { get; set; }
    public long? ResponseStartTs { get; set; }
    public ConcurrentQueue<string> MarkQueue { get; set; } = null!;
}