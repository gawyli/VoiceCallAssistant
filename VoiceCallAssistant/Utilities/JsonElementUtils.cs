using System.Text;
using System.Text.Json;

namespace VoiceCallAssistant.Utilities;

public static class JsonElementUtils
{
    public static JsonElement ExtractRootElementByte(this byte[] buffer, int count)
    {
        var jsonString = Encoding.UTF8.GetString(buffer, 0, count);
        using var doc = JsonDocument.Parse(jsonString);
        return doc.RootElement;
    }
}
