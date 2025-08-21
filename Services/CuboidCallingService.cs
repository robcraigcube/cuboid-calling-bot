using Cuboid.CallingBot.Models;

namespace Cuboid.CallingBot.Services;

public class CuboidCallingService
{
    private readonly GraphCallingClient _graph;
    private readonly AudioProcessingService _audio;
    private readonly ILogger<CuboidCallingService> _logger;
    private readonly Dictionary<string, CallSession> _activeCalls = new();

    public CuboidCallingService(
        GraphCallingClient graph,
        AudioProcessingService audio,
        ILogger<CuboidCallingService> logger)
    {
        _graph = graph;
        _audio = audio;
        _logger = logger;
    }

    public async Task ProcessNotificationAsync(CallbackNotification notification)
    {
        _logger.LogInformation("Processing notification: {Type} {Url}",
            notification.ChangeType, notification.ResourceUrl);

        try
        {
            var callId = ExtractCallId(notification.ResourceUrl);

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
                    _logger.LogInformation("Unhandled notification type: {Type}", notification.ChangeType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing notification for {Url}", notification.ResourceUrl);
        }
    }

    private async Task HandleIncomingCallAsync(string callId)
    {
        try
        {
            _logger.LogInformation("Handling incoming call: {CallId}", callId);

            var callbackUri = Environment.GetEnvironmentVariable("CALLBACK_URI")
                ?? "https://cuboid-calling-bot-rwc-axdpaqetgqd4aphz.uksouth-01.azurewebsites.net/api/calling";

            await _graph.AnswerAsync(callId, callbackUri);

            var session = new CallSession(callId);
            _activeCalls[callId] = session;

            await _audio.StartAudioProcessingAsync(session);

            await Task.Delay(2000);

            await _audio.SynthesizeAndPlayAsync(callId,
                "Hi all — Cuboid here. I'll stay on mute unless you say 'Cuboid'. " +
                "If you'd like me to stop speaking, say 'Cuboid, mute'.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error answering call {CallId}", callId);
            try { await _graph.RejectAsync(callId, "busy"); }
            catch (Exception rex) { _logger.LogError(rex, "Reject failed {CallId}", callId); }
        }
    }

    private async Task HandleCallEndedAsync(string callId)
    {
        _logger.LogInformation("Call ended: {CallId}", callId);
        if (_activeCalls.TryGetValue(callId, out var session))
        {
            session.Dispose();
            _activeCalls.Remove(callId);
        }
        await Task.CompletedTask;
    }

    private async Task HandleCallUpdateAsync(string callId, CallbackNotification _)
    {
        // Placeholder for state/participants/media updates
        _logger.LogDebug("Call updated: {CallId}", callId);
        await Task.CompletedTask;
    }

    public async Task HangupCallAsync(string callId)
    {
        await _graph.HangupAsync(callId);
        if (_activeCalls.TryGetValue(callId, out var session))
        {
            session.Dispose();
            _activeCalls.Remove(callId);
        }
    }

    private string ExtractCallId(string resourceUrl)
    {
        // Expected like "/communications/calls/{id}" -> take segment after "calls"
        try
        {
            var parts = resourceUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
                if (parts[i].Equals("calls", StringComparison.OrdinalIgnoreCase))
                    return parts[i + 1];
            return parts.LastOrDefault() ?? resourceUrl;
        }
        catch
        {
            return resourceUrl;
        }
    }

    public int GetActiveCallCount() => _activeCalls.Count;
}
