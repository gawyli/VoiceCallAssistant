using VoiceCallAssistant.Models;

namespace VoiceCallAssistant.Interfaces;

public interface IRoutineService
{
    Task<Routine?> GetRoutineByIdAsync(string routineId, CancellationToken cancellationToken);
}
