using Microsoft.Graph;
using Microsoft.Graph.Models; // Modality, RejectReason, AppHostedMediaConfig
using Cuboid.CallingBot.Models;
using Microsoft.Extensions.Logging;

// Aliases for the Graph v5 action request bodies (correct nested namespaces)
using AnswerBody  = Microsoft.Graph.Communications.Calls.Item.Answer.AnswerPostRequestBody;
using RejectBody  = Microsoft.Graph.Communications.Calls.Item.Reject.RejectPostRequestBody;
using HangupBody  = Microsoft.Graph.Communications.Calls.Item.Hangup.HangupPostRequestBody;

namespace Cuboid.CallingBot.Services;

public class CuboidCallingService
{
    private readonly GraphServiceClient _graphClient;
    private readonly AudioProcessingService _audioService;
    private readonly ILogger<CuboidCallingService> _logger;
    private readonly Dictionary<string, CallSession> _activeCalls;

    // REPLACE with your env var if you prefer:
    private const string WebhookBase = "https://cuboid-calling-bot-rwc-axdpaqetgqd4aphz.uksouth-01.azurewebsites.net";
    private static string CallbackUrl => $"{WebhookBase}/api/calling";

    public CuboidCallingService(
        GraphServiceClient graphClient,
        AudioProcessingService audioService,
        ILogger<CuboidCallingService> logger)
    {
        _graphClient = graphClient;
        _audioService = audioService;
        _logger = logger;
        _activeCalls = new Dictionary<string, CallSession>();
    }

    public async Task ProcessNotificationAsync(CallbackNotification notification)
    {
        _logger.LogInformation("Processing notification: {ChangeType} for {ResourceUrl}",
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
            _logger.LogError(ex, "Error processing notification for {ResourceUrl}", notification.ResourceUrl);
        }
    }

    private async Task HandleIncomingCallAsync(string callId)
    {
        try
        {
            _logger.LogInformation("Handling incoming call: {CallId}", callId);

            // Build the Answer request using CORRECT Graph v5 types/namespaces
            var answerRequest = new AnswerBody
            {
                CallbackUri = CallbackUrl,
                AcceptedModalities = new List<Modality?> { Modality.Audio },
                MediaConfig = new AppHostedMediaConfig
                {
                    // In a full implementation you provide a real app-hosted media configuration here
                    Blob = "app-hosted-media-config"
                    // NOTE: RemoveFromDefaultAudioGroup property is not present in v5 models. Do not set it.
                }
            };

            await _graphClient.Communications.Calls[callId].Answer.PostAsync(answerRequest);

            var session = new CallSession(callId);
            _activeCalls[callId] = session;

            _logger.LogInformation("Answered call: {CallId}", callId);

            await _audioService.StartAudioProcessingAsync(session);

            // Wait a bit for call to stabilise then send the join announcement
            await Task.Delay(2000);
            await SendJoinAnnouncementAsync(callId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error answering call {CallId}", callId);

            // If answer fails, try rejecting to be polite
            try
            {
                var reject = new RejectBody { Reason = RejectReason.Busy };
                await _graphClient.Communications.Calls[callId].Reject.PostAsync(reject);
            }
            catch (Exception rejectEx)
            {
                _logger.LogError(rejectEx, "Failed to reject call {CallId} after answer failure", callId);
            }
        }
    }

    private async Task HandleCallUpdateAsync(string callId, CallbackNotification notification)
    {
        try
        {
            _logger.LogInformation("Call updated: {CallId}", callId);

            if (_activeCalls.TryGetValue(callId, out _))
            {
                // TODO: handle call state/participant/media updates as needed
            }
            else
            {
                _logger.LogWarning("Received update for unknown call: {CallId}", callId);
            }
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
                _logger.LogInformation("Cleaned up session for call: {CallId}", callId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling call end for {CallId}", callId);
        }
    }

    private async Task SendJoinAnnouncementAsync(string callId)
    {
        try
        {
            var msg =
                "Hi all — Cuboid here. I'll stay on mute unless you say 'Cuboid'. " +
                "If you'd like me to go quiet, just say 'Cuboid, mute'.";

            await _audioService.SynthesizeAndPlayAsync(callId, msg);
            _logger.LogInformation("Sent join announcement for call: {CallId}", callId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending join announcement for {CallId}", callId);
        }
    }

    public async Task HangupCallAsync(string callId)
    {
        try
        {
            _logger.LogInformation("Hanging up call: {CallId}", callId);
            await _graphClient.Communications.Calls[callId].Hangup.PostAsync(new HangupBody());

            if (_activeCalls.TryGetValue(callId, out var session))
            {
                session.Dispose();
                _activeCalls.Remove(callId);
            }

            _logger.LogInformation("Successfully hung up call: {CallId}", callId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hanging up call {CallId}", callId);
        }
    }

    private static string ExtractCallId(string resourceUrl)
    {
        try
        {
            // Works for /communications/calls/{id} or /app/calls/{id}
            var parts = resourceUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("calls", StringComparison.OrdinalIgnoreCase))
                    return parts[i + 1];
            }
            return parts.LastOrDefault() ?? resourceUrl;
        }
        catch
        {
            return resourceUrl;
        }
    }

    public CallSession? GetCallSession(string callId) =>
        _activeCalls.TryGetValue(callId, out var s) ? s : null;

    public int GetActiveCallCount() => _activeCalls.Count;
}
