using Twilio;
using Microsoft.AspNetCore.Mvc;
using VoiceCallAssistant.Interfaces;
using Twilio.TwiML.Voice;
using VoiceCallAssistant.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace VoiceCallAssistant.Controllers;

[ApiController]
[Route("api/call")]
public class OutboundCallController : ControllerBase
{
    private readonly ITwilioService _twilioService;
    private readonly IRepository _repository;

    public OutboundCallController(ITwilioService twilioService, IRepository repository)
    {
        _twilioService = twilioService;
        _repository = repository;
    }

    [HttpPost("request", Name = "RequestOutboundCall")]
    public async Task<IActionResult> RequestOutboundCallPost([FromBody]CallRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.RoutineId))
        {
            return BadRequest("Routine ID cannot be null or empty.");
        }

        var routine = await _repository.GetByIdAsync<Routine>(request.RoutineId, cancellationToken);
        if (routine == null)
        {
            return NotFound($"Routine with ID {request.RoutineId} not found.");
        }

        _twilioService.CreateClient();
        var callSid = _twilioService.MakeCall(routine.PhoneNumber);

        if (string.IsNullOrEmpty(callSid))
        {
            return StatusCode(500, "Failed to initiate outbound call.");
        }

        return Ok("Outbound call request received successfully.");
    }

    [HttpPost("webhook", Name = "RequestOutboundCallWebhook")]
    public IActionResult RequestOutboundCallWebhookPost()
    {
        var request = new TwilioCallRequest();
        request.CallStatus = this.Request.Form["CallStatus"]!;
        request.To = this.Request.Form["To"]!;

        if (request.CallStatus == "completed")
        {
            Console.WriteLine("Call ended");
            return NoContent();
        }

        var htmlResponse = _twilioService.ConnectWebhook(this.Request);

        Console.WriteLine($"Webhook connected with response: {htmlResponse}");
        return Content(htmlResponse, "text/xml");
    }
}
