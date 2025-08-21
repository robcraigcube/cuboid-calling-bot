using Microsoft.AspNetCore.Mvc;
using Cuboid.CallingBot.Services;
using Cuboid.CallingBot.Models;

namespace Cuboid.CallingBot.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CallingController : ControllerBase
{
    private readonly CuboidCallingService _callingService;
    private readonly ILogger<CallingController> _logger;
    
    public CallingController(CuboidCallingService callingService, ILogger<CallingController> logger)
    {
        _callingService = callingService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> HandleCallback([FromBody] CallbackNotificationCollection? notifications)
    {
        try
        {
            var notificationCount = notifications?.Value?.Count ?? 0;
            _logger.LogInformation($"Received {notificationCount} notifications");
            
            if (notifications?.Value != null)
            {
                foreach (var notification in notifications.Value)
                {
                    if (notification != null)
                    {
                        await _callingService.ProcessNotificationAsync(notification);
                    }
                }
            }
            
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing callback notification");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        try
        {
            var activeCallCount = _callingService.GetActiveCallCount();
            
            return Ok(new 
            { 
                status = "operational",
                activeCalls = activeCallCount,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting status");
            return StatusCode(500, "Internal server error");
        }
    }
}
