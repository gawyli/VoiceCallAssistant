using System.ComponentModel.DataAnnotations;
using VoiceCallAssistant.Models;

namespace VoiceCallAssistant.Services;

public class RoutineService //: IRoutineService
{
    public RoutineService()
    {
        
    }

    public class RoutineModel
    {
        [Required]
        public string Username { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string ScheduledTime { get; set; } = null!;
        public bool IsMonFri { get; set; }
        [Required]
        public string PhoneNumber { get; set; } = null!;
        public Preferences Preferences { get; set; } = null!;
    }

    public static Routine CreateRoutine(RoutineModel routineModel)
    {
        if (routineModel == null)
        {
            throw new ArgumentNullException(nameof(routineModel), "Routine model cannot be null");
        }

        var scheduledTime = TimeOnly.Parse(routineModel.ScheduledTime);
        return new Routine(
            userProfileId: string.Empty,
            username: routineModel.Username,
            name: routineModel.Name,
            scheduledTime: scheduledTime,
            isMonFri: routineModel.IsMonFri,
            phoneNumber: routineModel.PhoneNumber,
            preferences: routineModel.Preferences ?? new Preferences());
    }
}
