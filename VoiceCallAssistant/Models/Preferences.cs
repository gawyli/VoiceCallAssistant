using VoiceCallAssistant.Utilities;

namespace VoiceCallAssistant.Models;

public class Preferences : ValueObject
{
    public string TopicOfInterest { get; set; }
    // TODO: Extract this to class of ToDos
    public string ToDos { get; set; }
    public string PersonalisedPrompt { get; set; }

    public Preferences(string topicOfInterest, string toDos, string personalisedPrompt)
    {
        this.TopicOfInterest = topicOfInterest;
        this.ToDos = toDos;
        this.PersonalisedPrompt = personalisedPrompt;
    }

    public Preferences() : this(string.Empty, string.Empty, string.Empty)
    {
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return this.TopicOfInterest;
        yield return this.ToDos;
        yield return this.PersonalisedPrompt;
    }
}
