using System.ComponentModel.DataAnnotations;

namespace VoiceCallAssistant.Models;

public class Routine : BaseEntity
{
    public string UserProfileId { get; set; } = null!;
    [Required]
    public string Username { get; set; } = null!;
    public string Name { get; set; } = null!;
    public TimeOnly ScheduledTime { get; set; }
    public bool IsMonFri { get; set; }
    [Required]
    public string PhoneNumber { get; set; } = null!;
    public Preferences Preferences { get; set; } = null!;

    public Routine(string userProfileId, string username, string name, TimeOnly scheduledTime, bool isMonFri, string phoneNumber, Preferences preferences)
    {
        this.UserProfileId = userProfileId;
        this.Username = username;
        this.Name = name;
        this.ScheduledTime = scheduledTime;
        this.IsMonFri = isMonFri;
        this.PhoneNumber = phoneNumber;
        this.Preferences = preferences;
    }

    // EF Core
    protected Routine()
    {
    }

}