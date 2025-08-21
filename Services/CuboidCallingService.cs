using Cuboid.CallingBot.Models;
using Microsoft.Extensions.Logging;

namespace Cuboid.CallingBot.Services;

public class CuboidCallingService
{
    private readonly AudioProcessingService _audioService;
    private readonly ILogger<CuboidCallingService> _logger;
    private readonly Dictionary<string, CallSession> _activeCalls = new();

    public CuboidCallingService(
        AudioProcessingService audioService,
        ILogger<CuboidCallingService> logger)
    {
        _audioService = audioService;
        _logger = logger;
    }

    public async Task ProcessNotificationAsync(CallbackNotification notification)
    {
        var callId = ExtractCallId(notification.ResourceUrl);
        _logger.LogInformation("Notification {Type} for {Resource} (callId: {CallId})",
            notification.ChangeType, notification.ResourceUrl, callId);

        try
        {
            switch (notification.ChangeType?.ToLowerInvariant())
            {
                case "created":
                    await HandleIncomingCallAsync(callId);
                    break;
                case "updated":
                    await HandleCallUpdateAsync(callId, notification);
                    break;
                case "deleted":
                    await HandleCallEndedAsync(callId);
                    break;
                default:
                    _logger.LogInformation("Ignoring changeType {Type}", notification.ChangeType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing notification for {Resource}", notification.ResourceUrl);
        }
    }

    private async Task HandleIncomingCallAsync(string callId)
    {
        _logger.LogInformation("Handling incoming call {CallId} (simulation – no Graph answer yet).", callId);

        // Track session locally
        var session = new CallSession(callId);
        _activeCalls[callId] = session;

        // Start “audio” pipeline
        await _audioService.StartAudioProcessingAsync(session);

        // Brief settle delay then play a greeting
        await Task.Delay(1000);
        await _audioService.SynthesizeAndPlayAsync(callId,
            "Hi all — Cuboid here. I'll stay on mute unless you say 'Cuboid'. If you'd like me to go quiet, just say 'Cuboid, mute'.");

        _logger.LogInformation("Call {CallId} prepared.", callId);
    }

    private Task HandleCallUpdateAsync(string callId, CallbackNotification _)
    {
        _logger.LogInformation("Call {CallId} updated (simulation).", callId);
        return Task.CompletedTask;
    }

    private Task HandleCallEndedAsync(string callId)
    {
        if (_activeCalls.TryGetValue(callId, out var session))
        {
            session.Dispose();
            _activeCalls.Remove(callId);
            _logger.LogInformation("Call {CallId} ended and cleaned up.", callId);
        }
        else
        {
            _logger.LogInformation("End received for unknown call {CallId}.", callId);
        }
        return Task.CompletedTask;
    }

    public Task HangupCallAsync(string callId)
    {
        // No real Graph hangup yet – just clean up.
        _logger.LogInformation("Hangup requested for {CallId} (simulation).", callId);
        return HandleCallEndedAsync(callId);
    }

    private static string ExtractCallId(string resourceUrl)
    {
        var parts = resourceUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], "calls", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        }
        return parts.LastOrDefault() ?? resourceUrl;
    }

    public int GetActiveCallCount() => _activeCalls.Count;

    public CallSession? GetCallSession(string callId) =>
        _activeCalls.TryGetValue(callId, out var s) ? s : null;
}
