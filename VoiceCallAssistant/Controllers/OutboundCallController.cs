using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Models;
using VoiceCallAssistant.Utilities;
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

    //[Authorize]
    //[RequiredScopeOrAppPermission(AcceptedAppPermission = new[] { "Request.Call" })]
    [HttpPost("request", Name = "RequestOutboundCall")]
    public async Task<IActionResult> RequestOutboundCallPost([FromBody]CallRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(request.RoutineId))
            {
                _logger.Warning("Routine ID is null or empty in the request.");
                return BadRequest("Routine ID cannot be null or empty.");
            }

            _logger.Information("Received outbound call request for RoutineId: {RoutineId}", request.RoutineId);
            
            var routine = await _repository.GetByIdAsync<Routine>(request.RoutineId, cancellationToken);
            if (routine == null)
            {
                _logger.Warning("Routine with ID {RoutineId} not found in the repository.", request.RoutineId);
                return NotFound($"Routine with ID {request.RoutineId} not found.");
            }

            _twilioService.CreateClient();
            _logger.Information("Twilio client created successfully.");

            var callSid = _twilioService.MakeCall(routine.PhoneNumber, routine.Id);
            if (string.IsNullOrEmpty(callSid))
            {
                _logger.Error("Failed to initiate outbound call for RoutineId: {RoutineId}", routine.Id);
                throw new ArgumentNullException("Failed to obtain call SID.");
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
    public async Task<IActionResult> RequestOutboundCallWebhookPost()
    {
        try
        {
            var form = await HttpContext.Request.ReadFormAsync();
            var headers = this.Request.Headers;
            var url = this.Request.GetDisplayUrl();
            var routineId = this.Request.Path.GetLastItem('/');

            // When using GitHub Codespaces, the URL has to be set manaully in order to use Twilio Verification.
            // var url = $"https://<github-codespaces-url-xxx>-5055.app.github.dev/api/call/webhook/{routineId}";
            // if (!_twilioService.ValidateRequest(url, headers, form))
            // {
            //     _logger.Warning("Invalid Twilio request signature for RoutineId: {RoutineId}", routineId);
            //     return Unauthorized("Invalid request signature.");
            // }

            var request = new TwilioCallRequest
            {
                CallStatus = form["CallStatus"]!
            };

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
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create TwiML for webhook.");
            return StatusCode(500, "Failed to create TwiML.");
        }
    }
}
