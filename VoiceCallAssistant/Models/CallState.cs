using System.Collections.Concurrent;

namespace VoiceCallAssistant.Models;
public class CallState
{
    public string? StreamSid { get; set; }
    public TimeSpan StreamDurationTimestamp { get; set; }
    public string? LastAssistantId { get; set; }
    public int? ContentPartsIndex { get; set; }
    public TimeSpan? ResponseStartTs { get; set; }
    public ConcurrentQueue<string> MarkQueue { get; set; } = null!;

    public void Clear()
    {
        LastAssistantId = null;
        ContentPartsIndex = null;
        ResponseStartTs = null;
        MarkQueue.Clear();
    }
}