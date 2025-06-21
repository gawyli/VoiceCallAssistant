namespace VoiceCallAssistant.Models;

public class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    /// <summary>
    /// OpenAI API key, see https://platform.openai.com/account/api-keys
    /// </summary>
    public string? ApiKey { get; set; }
    public string? Model { get; set; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(this.ApiKey) && !string.IsNullOrWhiteSpace(this.Model);
}
