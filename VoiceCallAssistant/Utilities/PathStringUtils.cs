namespace VoiceCallAssistant.Utilities;

public static class PathStringUtils
{
    public static string GetLastItem(this PathString pathString, char split)
    {
        var path = pathString.ToString();
        string[] partsPath = path.Split(split);
        var lastItem = partsPath[partsPath.Length-1];

        return lastItem;
    }
}