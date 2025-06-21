using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Models;
using VoiceCallAssistant.Services;
using static VoiceCallAssistant.Services.RoutineService;
using ILogger = Serilog.ILogger;

namespace VoiceCallAssistant.Controllers;

[ApiController]
[Route("api/routine")]
public class RoutineController : ControllerBase
{
    private readonly ILogger _logger;
    private readonly IRepository _repository;

    public RoutineController(ILogger logger, IRepository repository)    // TODO: Move data access to RoutineService
    {
        _logger = logger;
        _repository = repository;
    }

    [HttpGet("get", Name = "GetRoutine")]
    public async Task<IActionResult> GetRoutine([FromQuery]string routineId, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(routineId))
            {
                _logger.Error("Routine ID is null or empty");
                return BadRequest("Routine ID cannot be null or empty.");
            }

            var routine = await _repository.GetByIdAsync<Routine>(routineId, cancellationToken);
            if (routine == null)
            {
                _logger.Warning("Routine with ID {Id} not found", routineId);
                return NotFound($"Routine with ID {routineId} not found.");
            }

            _logger.Information("Routine retrieved with ID: {Id}", routineId);
            return Ok(routine);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error retrieving routine with ID: {Id}", routineId);
            return StatusCode(500, "An error occurred while retrieving the routine.");
        }

    }

    [HttpGet("list", Name = "GetRoutines")]
    public async Task<IActionResult> GetRoutines(CancellationToken cancellationToken)
    {
        try
        {
            var routines = await _repository.ListAsync<Routine>(cancellationToken);

            _logger.Information("Retrieved {Count} routines", routines.Count);
            return Ok(routines);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error retrieving routines");
            return StatusCode(500, "An error occurred while retrieving routines.");
        }

    }

    [HttpPost("create", Name = "CreateRoutine")]
    public async Task<IActionResult> CreateRoutine([FromBody]RoutineModel routineModel, CancellationToken cancellationToken)
    {
        try
        {
            var routine = await _repository.AddAsync(RoutineService.CreateRoutine(routineModel), cancellationToken);
            if (routine == null)
            {
                _logger.Error("Failed to create routine");
                return BadRequest("Failed to create routine.");
            }

            _logger.Information("Routine created with ID: {Id}", routine.Id);
            return CreatedAtAction(nameof(CreateRoutine), new { id = routine.Id }, routine);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error creating routine");
            return StatusCode(500, "Error creating routine");
        }

    }

    [HttpDelete("remove", Name = "RemoveRoutine")]
    public async Task<IActionResult> RemoveRoutine([FromQuery]string routineId, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(routineId))
            {
                _logger.Error("Routine ID is null or empty");
                return BadRequest("Routine ID cannot be null or empty.");
            }

            var routine = await _repository.GetByIdAsync<Routine>(routineId, cancellationToken);
            if (routine == null)
            {
                _logger.Warning("Routine with ID {Id} not found", routineId);
                return NotFound($"Routine with ID {routineId} not found.");
            }

            await _repository.DeleteAsync(routine, cancellationToken);

            _logger.Information("Routine with ID {Id} removed successfully", routineId);
            return Ok($"Routine with ID {routineId} removed successfully.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error removing routine with ID: {Id}", routineId);
            return StatusCode(500, "An error occurred while removing the routine.");
        }
    }
}
