using System.ComponentModel.DataAnnotations;
using VoiceCallAssistant.Models;
using VoiceCallAssistant.Interfaces;
using ILogger = Serilog.ILogger;

namespace VoiceCallAssistant.Services;

public class RoutineService : IRoutineService
{
    private readonly ILogger _logger;
    private readonly IRepository _repository;

    public RoutineService(ILogger logger, IRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<Routine?> GetRoutineByIdAsync(string routineId, CancellationToken cancellationToken)
    {
        var routine = await _repository.GetByIdAsync<Routine>(routineId, cancellationToken);

        return routine;
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
