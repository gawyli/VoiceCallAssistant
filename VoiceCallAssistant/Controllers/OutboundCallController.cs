using Twilio;
using Microsoft.AspNetCore.Mvc;
using VoiceCallAssistant.Interfaces;
using Twilio.TwiML.Voice;

namespace VoiceCallAssistant.Controllers;

[ApiController]
[Route("[controller]")]
public class OutboundCallController : ControllerBase
{
    private readonly ITwilioService _twilioService;

    public OutboundCallController(ITwilioService twilioService)
    {
        _twilioService = twilioService;
    }

    [HttpPost(Name = "RequestOutboundCall")]
    public IActionResult RequestOutboundCallPost([FromBody]string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest("User ID cannot be null or empty.");
        }

        // TODO: Fetch user details from the repository
        //var user = _repository.GetUserById(userId);

        _twilioService.CreateClient();
        var sip = _twilioService.MakeCall("user.PhoneNumber");

        if (string.IsNullOrEmpty(sip))
        {
            return StatusCode(500, "Failed to initiate outbound call.");
        }

        return Ok("Outbound call request received successfully.");
    }

    [HttpPost(Name = "RequestOutboundCallWebhook")]
    public IActionResult RequestOutboundCallWebhookPost() //Grab request and get the phone number from the request body
    {

        // TODO: Fetch user details from the repository
        //var user = _repository.GetUserById(userId);

        var htmlResponse = _twilioService.ConnectWebhook("+447402033899");

        Console.WriteLine($"Webhook connected with response: {htmlResponse}");

        return Ok("Outbound call request received successfully.");
    }
}
