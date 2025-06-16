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

    public OutboundCallController(ITwilioService twilioService)
    {
        _twilioService = twilioService;
    }

    [HttpPost("request", Name = "RequestOutboundCall")]
    public IActionResult RequestOutboundCallPost([FromBody]CallRequest request)
    {
        if (string.IsNullOrEmpty(request.UserId))
        {
            return BadRequest("User ID cannot be null or empty.");
        }

        // TODO: Fetch user details from the repository
        //var user = _repository.GetUserById(userId);

        _twilioService.CreateClient();
        var sip = _twilioService.MakeCall("+447402033899");

        if (string.IsNullOrEmpty(sip))
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

        var htmlResponse = _twilioService.ConnectWebhook();

        Console.WriteLine($"Webhook connected with response: {htmlResponse}");
        return Content(htmlResponse, "text/xml");
    }
}
