//using Microsoft.AspNetCore.Mvc;
//using VoiceCallAssistant.Interfaces;
//using VoiceCallAssistant.Models;
//using ILogger = Serilog.ILogger;

//namespace VoiceCallAssistant.Controllers;

//[ApiController]
//[Route("api/routine")]   //For testing purposes
//public class RoutineController : ControllerBase
//{
//    private readonly ILogger _logger;
//    private readonly IRepository _repository;

//    public RoutineController(ILogger logger, IRepository repository)
//    {
//        _logger = logger;
//        _repository = repository;
//    }

//    [HttpPost("create", Name = "CreateRoutine")]
//    public async Task<IActionResult> CreateRoutinePost(CancellationToken cancellationToken)
//    {
//        try
//        {
//            var routine = CreateRoutine();
//            await _repository.AddAsync(routine, cancellationToken);

//            _logger.Information("Routine created with ID: {Id}", routine.Id);
//            return CreatedAtAction(nameof(CreateRoutinePost), new { id = routine.Id }, routine);
//        }
//        catch (Exception ex)
//        {
//            _logger.Error(ex, "Error creating routine");
//            throw new ArgumentNullException("Error creating routine", ex);
//        }
        
//    }

//    private Routine CreateRoutine()
//    {
//        throw new Exception("This method is not implemented yet. Please implement the logic to create a routine.");

//        //return new Routine(
//        //    "default-user-profile-id",
//        //    "default-username",
//        //    "default-routine",
//        //    new TimeOnly(9, 0), // 9:00 AM
//        //    true,
//        //    "+447402054824",
//        //    new Preferences
//        //    {
//        //        TopicOfInterest = "default-topic",
//        //        ToDos = "default-todos",
//        //        PersonalisedPrompt = "default-prompt"
//        //    });

//    }
//}
