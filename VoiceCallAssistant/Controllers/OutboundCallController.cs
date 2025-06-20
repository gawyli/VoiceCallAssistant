using Twilio;
using Microsoft.AspNetCore.Mvc;
using VoiceCallAssistant.Interfaces;
using Twilio.TwiML.Voice;
using VoiceCallAssistant.Models;
using VoiceCallAssistant.Utilities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web.Resource;
using ILogger = Serilog.ILogger;

namespace VoiceCallAssistant.Controllers;

[ApiController]
[Route("api/call")]
//[Authorize()]
public class OutboundCallController : ControllerBase
{
    private readonly ILogger _logger;
    private readonly ITwilioService _twilioService;
    private readonly IRepository _repository;

    public OutboundCallController(ILogger logger, ITwilioService twilioService, IRepository repository)
    {
        _logger = logger;
        _twilioService = twilioService;
        _repository = repository;
    }

    //[RequiredScope("call.request")]
    [HttpPost("request", Name = "RequestOutboundCall")]
    public async Task<IActionResult> RequestOutboundCallPost([FromBody]CallRequest request, CancellationToken cancellationToken)
    {
        _logger.Information("Received outbound call request for RoutineId: {RoutineId}", request.RoutineId);

        if (string.IsNullOrEmpty(request.RoutineId))
        {
            _logger.Warning("Routine ID is null or empty in the request.");
            return BadRequest("Routine ID cannot be null or empty.");
        }

        var routine = await _repository.GetByIdAsync<Routine>(request.RoutineId, cancellationToken);
        if (routine == null)
        {
            _logger.Warning("Routine with ID {RoutineId} not found in the repository.", request.RoutineId);
            return NotFound($"Routine with ID {request.RoutineId} not found.");
        }
        
        try
        {
            _twilioService.CreateClient();
            _logger.Information("Twilio client created successfully.");

            var callSid = _twilioService.MakeCall(routine.PhoneNumber, routine.Id);
            if (string.IsNullOrEmpty(callSid))
            {
                _logger.Error("Failed to initiate outbound call for RoutineId: {RoutineId}", routine.Id);
                return StatusCode(500, "Failed to initiate outbound call.");
            }

            _logger.Information("Outbound call initiated. CallSid: {CallSid}, RoutineId: {RoutineId}", callSid, routine.Id);
            return Ok("Outbound call request received successfully.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error during outbound call request for RoutineId: {RoutineId}", request.RoutineId);
            return StatusCode(500, "An error occurred while processing the call request.");
        }
        
    }

    [HttpPost("webhook/{routineId}", Name = "RequestOutboundCallWebhook")]
    public IActionResult RequestOutboundCallWebhookPost()
    {
        // TODO: Activate validation once deployed
        // if (!_twilioService.ValidateRequest(this.Request))
        // {
        //     _logger.Warrning("Invalid Twilio request signature for RoutineId: {RoutineId}", routineId);
        //     return Unauthorized("Invalid request signature.");
        // }

        var request = new TwilioCallRequest
        {
            CallStatus = this.Request.Form["CallStatus"]!
        };

        var routineId = this.Request.Path.GetLastItem('/');
        if (string.IsNullOrEmpty(routineId))
        {
            _logger.Warning("Routine ID is null or empty in the webhook request.");
            return BadRequest("Routine ID cannot be null or empty.");
        }

        _logger.Information("Received Twilio webhook for RoutineId: {RoutineId}, CallStatus: {CallStatus}", routineId, request.CallStatus);

        if (request.CallStatus == "completed")
        {
            _logger.Information("Call for RoutineId: {RoutineId} has completed.", routineId);
            return NoContent();
        }

        var htmlResponse = _twilioService.ConnectWebhook(routineId);
        _logger.Debug("Generated TwiML for RoutineId: {RoutineId}. Response: {Response}", routineId, htmlResponse);

        return Content(htmlResponse, "text/xml");
    }
}
