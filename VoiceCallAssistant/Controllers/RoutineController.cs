using Microsoft.AspNetCore.Mvc;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Models;

namespace VoiceCallAssistant.Controllers;

[ApiController]
[Route("api/routine")]
public class RoutineController : ControllerBase
{
    private readonly IRepository _repository;

    public RoutineController(IRepository repository)
    {
        _repository = repository;
    }

    [HttpPost("create", Name = "CreateRoutine")]
    public async Task<IActionResult> CreateRoutinePost(CancellationToken cancellationToken)
    {
        var routine = CreateRoutine();
        await _repository.AddAsync(routine, cancellationToken);

        return CreatedAtAction(nameof(CreateRoutinePost), new { id = routine.Id }, routine);
    }

    private Routine CreateRoutine()
    {
        return new Routine(
            "default-user-profile-id",
            "default-username",
            "default-routine",
            new TimeOnly(9, 0), // 9:00 AM
            true,
            "+447402033899",
            new Preferences
            {
                TopicOfInterest = "default-topic",
                ToDos = "default-todos",
                PersonalisedPrompt = "default-prompt"
            });
        
    }
}
