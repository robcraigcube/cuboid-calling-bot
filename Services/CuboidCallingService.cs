using Microsoft.Graph;
using G = Microsoft.Graph.Models;          // alias to Graph models
using Cuboid.CallingBot.Models;

namespace Cuboid.CallingBot.Services;

public class CuboidCallingService
{
    private readonly GraphServiceClient _graphClient;
    private readonly AudioProcessingService _audioService;
    private readonly ILogger<CuboidCallingService> _logger;
    private readonly Dictionary<string, CallSession> _activeCalls = new();

    public CuboidCallingService(
        GraphServiceClient graphClient,
        AudioProcessingService audioService,
        ILogger<CuboidCallingService> logger)
    {
        _graphClient = graphClient;
        _audioService = audioService;
        _logger = logger;
    }

    public async Task ProcessNotificationAsync(CallbackNotification notification)
    {
        _logger.LogInformation("Processing notification: {Type} for {Url}", notification.ChangeType, notification.ResourceUrl);

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
            _logger.LogInformation("Answering incoming call: {CallId}", callId);

            // Build request using Microsoft.Graph.Models
            var answer = new G.AnswerPostRequestBody
            {
                CallbackUri = "https://cuboid-calling-bot-rwc-axdpaqetgqd4aphz.uksouth-01.azurewebsites.net/api/calling",
                AcceptedModalities = new List<G.Modality?> { G.Modality.Audio },
                // NOTE: 'RemoveFromDefaultAudioGroup' is not present in stable Graph v5 type. Omit it.
                MediaConfig = new G.AppHostedMediaConfig
                {
                    // Your media config blob (placeholder string is fine for now)
                    Blob = "app-hosted-media-config"
                }
            };

            await _graphClient.Communications.Calls[callId].Answer.PostAsync(answer);

            // Track session
            var session = new CallSession(callId);
            _activeCalls[callId] = session;

            // Start audio processing
            await _audioService.StartAudioProcessingAsync(session);

            // Give the call a moment to settle, then play a greeting
            await Task.Delay(2000);
            await SendJoinAnnouncementAsync(callId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error answering call {CallId}", callId);

            // Best-effort reject if answer fails
            try
            {
                await _graphClient.Communications.Calls[callId].Reject.PostAsync(
                    new G.RejectPostRequestBody { Reason = G.RejectReason.Busy });
            }
            catch (Exception rejectEx)
            {
                _logger.LogError(rejectEx, "Failed to reject call {CallId} after answer failure", callId);
            }
        }
    }

    private async Task HandleCallUpdateAsync(string callId, CallbackNotification _)
    {
        try
        {
            _logger.LogInformation("Call updated: {CallId}", callId);

            if (_activeCalls.TryGetValue(callId, out var _))
            {
                // Placeholder for future call-state/media handling
            }
            else
            {
                _logger.LogWarning("Received update for unknown call: {CallId}", callId);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling call update for {CallId}", callId);
        }
    }

    private async Task HandleCallEndedAsync(string callId)
    {
        try
        {
            _logger.LogInformation("Call ended: {CallId}", callId);

            if (_activeCalls.TryGetValue(callId, out var session))
            {
                session.Dispose();
                _activeCalls.Remove(callId);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling call end for {CallId}", callId);
        }
    }

    private async Task SendJoinAnnouncementAsync(string callId)
    {
        var msg = "Hi all — Cuboid here. I'll stay on mute unless you say 'Cuboid'. " +
                  "If you'd like me to go quiet, say 'Cuboid, mute'.";

        await _audioService.SynthesizeAndPlayAsync(callId, msg);
        _logger.LogInformation("Join announcement sent for {CallId}", callId);
    }

    public async Task HangupCallAsync(string callId)
    {
        // Keep simple for now to avoid API mismatches; add proper hangUp action later if needed.
        _logger.LogInformation("Requested hangup for {CallId}", callId);
        await Task.CompletedTask;
    }

    private static string ExtractCallId(string resourceUrl)
    {
        if (string.IsNullOrWhiteSpace(resourceUrl)) return string.Empty;

        var segments = resourceUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("calls", StringComparison.OrdinalIgnoreCase))
                return segments[i + 1];
        }

        return segments.LastOrDefault() ?? resourceUrl;
    }

    public CallSession? GetCallSession(string callId) =>
        _activeCalls.TryGetValue(callId, out var s) ? s : null;

    public int GetActiveCallCount() => _activeCalls.Count;
}
