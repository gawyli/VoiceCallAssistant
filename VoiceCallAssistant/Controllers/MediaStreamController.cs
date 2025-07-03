using Microsoft.AspNetCore.Mvc;
using OpenAI.RealtimeConversation;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Models;
using VoiceCallAssistant.Utilities;
using ILogger = Serilog.ILogger;

namespace VoiceCallAssistant.Controllers;

[ApiController]
[Route("/ws")]
public class MediaStreamController : ControllerBase
{
    private readonly ILogger _logger;
    private readonly IVoiceCallService _voiceCallService;
    private readonly IRoutineService _routineService;

    public MediaStreamController(ILogger logger, IVoiceCallService voiceCallService, IRoutineService routineService)
    {
        _logger = logger;
        _voiceCallService = voiceCallService;
        _routineService = routineService;

    }

    [HttpGet("media-stream/{routineId}", Name = "MediaStreamWebsocket")]
    public async Task MediaStreamWebsocketGet(CancellationToken cancellationToken)
    {
        try
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                _logger.Warning("Rejected non-WebSocket request to media-stream endpoint.");
                this.HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // TODO: Research how validate websocket
            // if (!_twilioService.ValidateRequest(this.Request))
            // {
            //     HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            //     return;
            // }

            var routineId = this.Request.Path.GetLastItem('/');
            if (string.IsNullOrEmpty(routineId))
            {
                _logger.Warning("Routine ID was not provided or invalid in WebSocket request path.");
                this.HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }  

            var routine = await _routineService.GetRoutineByIdAsync(routineId, cancellationToken);
            if (routine == null)
            {
                _logger.Warning("Routine not found for Routine ID: {RoutineId}", routineId);
                return;
            }
            // TODO: Add Interests and Tasks
            var userPrompt = $"<PersonalisedPrompt> {routine.Preferences.PersonalisedPrompt} </PersonalisedPrompt>";

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var realtimeSocket = await _voiceCallService.CreateConversationSession(userPrompt, linkedCts);
            using var twilioSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            _logger.Information("WebSocket connection established.");

            await _voiceCallService.OrchestrateAsync(twilioSocket, realtimeSocket, linkedCts);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error Handling Media Stream.");
            this.HttpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }
    }

}