using Cuboid.CallingBot.Models;

namespace Cuboid.CallingBot.Services;

public class CuboidCallingService
{
    // NOTE: We are not using the Graph Calling SDK here. This class keeps the shape/flow
    // and logs actions so the web API compiles and deploys cleanly.
    private readonly AudioProcessingService _audioService;
    private readonly ILogger<CuboidCallingService> _logger;
    private readonly Dictionary<string, CallSession> _activeCalls;

    public CuboidCallingService(
        AudioProcessingService audioService,
        ILogger<CuboidCallingService> logger)
    {
        _audioService = audioService;
        _logger = logger;
        _activeCalls = new Dictionary<string, CallSession>();
    }

    public async Task ProcessNotificationAsync(CallbackNotification notification)
    {
        _logger.LogInformation("Processing notification: {Type} for {Url}",
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

            // Placeholder "answer" — in production this becomes a real Graph Calls Answer.
            var answer = new AnswerPostRequestBody
            {
                CallbackUri = "https://cuboid-calling-bot-rwc-axdpaqetgqd4aphz.uksouth-01.azurewebsites.net/api/calling",
                AcceptedModalities = new List<Modality?> { Modality.Audio },
                MediaConfig = new AppHostedMediaConfig
                {
                    Blob = GenerateMediaConfigBlob(),
                    RemoveFromDefaultAudioGroup = false
                }
            };
            _logger.LogInformation("Pretend-answer sent with callback {Callback}", answer.CallbackUri);

            // Track session
            var session = new CallSession(callId);
            _activeCalls[callId] = session;

            // Start audio processing (stub)
            await _audioService.StartAudioProcessingAsync(session);

            // Give the call time to stabilise before an intro message
            await Task.Delay(2000);

            await SendJoinAnnouncementAsync(callId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error answering call {CallId}. Will pretend-reject as Busy.", callId);
            // Placeholder reject
            var reject = new RejectPostRequestBody { Reason = RejectReason.Busy };
            _logger.LogInformation("Pretend-reject sent for {CallId} with reason {Reason}", callId, reject.Reason);
        }
    }

    private Task HandleCallUpdateAsync(string callId, CallbackNotification notification)
    {
        if (_activeCalls.TryGetValue(callId, out _))
            _logger.LogDebug("Call updated for session {CallId}", callId);
        else
            _logger.LogWarning("Received update for unknown call: {CallId}", callId);

        return Task.CompletedTask;
    }

    private Task HandleCallEndedAsync(string callId)
    {
        _logger.LogInformation("Call ended: {CallId}", callId);

        if (_activeCalls.TryGetValue(callId, out var session))
        {
            session.Dispose();
            _activeCalls.Remove(callId);
            _logger.LogInformation("Cleaned up session for call: {CallId}", callId);
        }

        return Task.CompletedTask;
    }

    private async Task SendJoinAnnouncementAsync(string callId)
    {
        try
        {
            var msg = "Hi all — Cuboid here. I'll stay on mute unless you say 'Cuboid'. " +
                      "If you'd like me to go quiet, say 'Cuboid, mute'.";
            await _audioService.SynthesizeAndPlayAsync(callId, msg);
            _logger.LogInformation("Join announcement sent for {CallId}", callId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending join announcement for {CallId}", callId);
        }
    }

    public Task HangupCallAsync(string callId)
    {
        // Placeholder hangup
        _logger.LogInformation("Pretend-hangup for {CallId}", callId);

        if (_activeCalls.TryGetValue(callId, out var session))
        {
            session.Dispose();
            _activeCalls.Remove(callId);
        }

        return Task.CompletedTask;
    }

    private static string ExtractCallId(string resourceUrl)
    {
        try
        {
            var segments = resourceUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].Equals("calls", StringComparison.OrdinalIgnoreCase))
                    return segments[i + 1];
            }
            return segments.LastOrDefault() ?? resourceUrl;
        }
        catch
        {
            return resourceUrl;
        }
    }

    private static string GenerateMediaConfigBlob() => "application-hosted-media-config";

    public CallSession? GetCallSession(string callId) =>
        _activeCalls.TryGetValue(callId, out var s) ? s : null;

    public int GetActiveCallCount() => _activeCalls.Count;
}
