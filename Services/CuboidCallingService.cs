using Microsoft.Graph;
using Microsoft.Graph.Models; // Modality, RejectReason, AppHostedMediaConfig
using Cuboid.CallingBot.Models;

using AnswerPostRequestBody = Microsoft.Graph.Communications.Calls.Item.Answer.AnswerPostRequestBody;
using RejectPostRequestBody = Microsoft.Graph.Communications.Calls.Item.Reject.RejectPostRequestBody;

namespace Cuboid.CallingBot.Services;

public class CuboidCallingService
{
    private readonly GraphServiceClient _graphClient;
    private readonly AudioProcessingService _audioService;
    private readonly ILogger<CuboidCallingService> _logger;
    private readonly Dictionary<string, CallSession> _activeCalls;

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
            _logger.LogInformation("Answering call: {CallId}", callId);

            var answerRequest = new AnswerPostRequestBody
            {
                CallbackUri = "https://cuboid-calling-bot-rwc-axdpaqetgqd4aphz.uksouth-01.azurewebsites.net/api/calling",
                AcceptedModalities = new List<Modality?> { Modality.Audio },
                MediaConfig = new AppHostedMediaConfig
                {
                    // In a real implementation you’d provide the app-hosted media blob.
                    Blob = "application-hosted-media-config"
                }
            };

            await _graphClient.Communications.Calls[callId]
                .Answer
                .PostAsync(answerRequest);

            var session = new CallSession(callId);
            _activeCalls[callId] = session;

            await _audioService.StartAudioProcessingAsync(session);

            await Task.Delay(2000); // let the call stabilize
            await SendJoinAnnouncementAsync(callId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Answer failed for {CallId}; attempting Reject(Busy)", callId);
            try
            {
                var reject = new RejectPostRequestBody { Reason = RejectReason.Busy };
                await _graphClient.Communications.Calls[callId]
                    .Reject
                    .PostAsync(reject);
            }
            catch (Exception rex)
            {
                _logger.LogError(rex, "Reject also failed for {CallId}", callId);
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
                // hook state, participants, media updates here in a full implementation
            }
            else
            {
                _logger.LogWarning("Update for unknown call: {CallId}", callId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update for {CallId}", callId);
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up ended call {CallId}", callId);
        }
    }

    private async Task SendJoinAnnouncementAsync(string callId)
    {
        try
        {
            var msg = "Hi all — Cuboid here. I'll stay on mute unless you say 'Cuboid'. Say 'Cuboid, mute' to silence me.";
            await _audioService.SynthesizeAndPlayAsync(callId, msg);
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

            // Kiota prefixes Graph actions with MicrosoftGraph*
            await _graphClient.Communications.Calls[callId]
                .MicrosoftGraphHangUp
                .PostAsync();

            if (_activeCalls.TryGetValue(callId, out var session))
            {
                session.Dispose();
                _activeCalls.Remove(callId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hanging up {CallId}", callId);
        }
    }

    private string ExtractCallId(string resourceUrl)
    {
        try
        {
            var parts = resourceUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
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
