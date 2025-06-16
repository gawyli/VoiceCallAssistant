using Twilio;
using Microsoft.AspNetCore.Mvc;
using VoiceCallAssistant.Interfaces;
using Twilio.TwiML.Voice;
using VoiceCallAssistant.Models;

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
        var sip = _twilioService.MakeCall("xxx");

        if (string.IsNullOrEmpty(sip))
        {
            return StatusCode(500, "Failed to initiate outbound call.");
        }

        return Ok("Outbound call request received successfully.");
    }

    [HttpPost("webhook", Name = "RequestOutboundCallWebhook")]
    public IActionResult RequestOutboundCallWebhookPost() //Grab request and get the phone number from the request body
    {

        // TODO: Fetch user details from the repository
        //var user = _repository.GetUserById(userId);

        var htmlResponse = _twilioService.ConnectWebhook("xxx");

        Console.WriteLine($"Webhook connected with response: {htmlResponse}");

        return Ok(htmlResponse);
    }
}
