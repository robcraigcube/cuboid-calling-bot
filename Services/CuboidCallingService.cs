using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Cuboid.CallingBot.Models;

//
// IMPORTANT: Graph v5 action namespaces for calls
//
using AnswerNS = Microsoft.Graph.Communications.Calls.Item.Answer;
using RejectNS = Microsoft.Graph.Communications.Calls.Item.Reject;
using HangupNS = Microsoft.Graph.Communications.Calls.Item.MicrosoftGraphHangup;

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
        _logger.LogInformation("Processing notification: {type} for {url}",
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
                    _logger.LogInformation("Unhandled notification type: {type}", notification.ChangeType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing notification for {url}", notification.ResourceUrl);
        }
    }

    private async Task HandleIncomingCallAsync(string callId)
    {
        try
        {
            _logger.LogInformation("Answering incoming call: {callId}", callId);

            // Graph v5: action request body for /communications/calls/{id}/answer
            var answerRequest = new AnswerNS.AnswerPostRequestBody
            {
                CallbackUri = "https://cuboid-calling-bot-rwc-axdpaqetgqd4aphz.uksouth-01.azurewebsites.net/api/calling",
                AcceptedModalities = new List<Modality?> { Modality.Audio },
                MediaConfig = new AppHostedMediaConfig
                {
                    Blob = GenerateMediaConfigBlob(),
                    RemoveFromDefaultAudioGroup = false
                }
            };

            await _graphClient.Communications
                .Calls[callId]
                .Answer
                .PostAsync(answerRequest);

            // Track session
            var session = new CallSession(callId);
            _activeCalls[callId] = session;

            _logger.LogInformation("Call answered: {callId}", callId);

            // Start audio and announce
            await _audioService.StartAudioProcessingAsync(session);
            await Task.Delay(2000); // settle
            await SendJoinAnnouncementAsync(callId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error answering call {callId}", callId);

            // Best-effort reject if answer failed
            try
            {
                await _graphClient.Communications
                    .Calls[callId]
                    .Reject
                    .PostAsync(new RejectNS.RejectPostRequestBody
                    {
                        Reason = RejectReason.Busy
                    });
            }
            catch (Exception rejectEx)
            {
                _logger.LogError(rejectEx, "Reject also failed for call {callId}", callId);
            }
        }
    }

    private async Task HandleCallUpdateAsync(string callId, CallbackNotification notification)
    {
        try
        {
            _logger.LogInformation("Call updated: {callId}", callId);

            if (_activeCalls.TryGetValue(callId, out var session))
            {
                // TODO: handle state/media/participants changes when you wire full Graph Calling
                _logger.LogDebug("Update processed for active session {callId}", callId);
            }
            else
            {
                _logger.LogWarning("Update received for unknown call {callId}", callId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling call update for {callId}", callId);
        }
    }

    private async Task HandleCallEndedAsync(string callId)
    {
        try
        {
            _logger.LogInformation("Call ended: {callId}", callId);

            if (_activeCalls.Remove(callId, out var session))
            {
                session.Dispose();
                _logger.LogInformation("Session cleaned for {callId}", callId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up call {callId}", callId);
        }
    }

    private async Task SendJoinAnnouncementAsync(string callId)
    {
        try
        {
            var msg = "Hi all — Cuboid here. It's great to be out of the chat and actually use my voice. " +
                      "I'll stay on mute unless you say 'Cuboid'. If you'd like me to leave or go quiet, just say 'Cuboid, mute'.";

            await _audioService.SynthesizeAndPlayAsync(callId, msg);
            _logger.LogInformation("Join announcement sent for {callId}", callId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending join announcement for {callId}", callId);
        }
    }

    public async Task HangupCallAsync(string callId)
    {
        try
        {
            _logger.LogInformation("Hanging up {callId}", callId);

            await _graphClient.Communications
                .Calls[callId]
                .MicrosoftGraphHangup
                .PostAsync(new HangupNS.MicrosoftGraphHangupPostRequestBody
                {
                    ClientContext = Guid.NewGuid().ToString()
                });

            if (_activeCalls.Remove(callId, out var session))
            {
                session.Dispose();
            }

            _logger.LogInformation("Hung up {callId}", callId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hanging up {callId}", callId);
        }
    }

    private string ExtractCallId(string resourceUrl)
    {
        try
        {
            // e.g. /communications/calls/{id}
            var segments = resourceUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].Equals("calls", StringComparison.OrdinalIgnoreCase))
                    return segments[i + 1];
            }
            return segments.LastOrDefault() ?? resourceUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse call id from {url}", resourceUrl);
            return resourceUrl;
        }
    }

    private string GenerateMediaConfigBlob() => "application-hosted-media-config";

    public CallSession? GetCallSession(string callId) =>
        _activeCalls.TryGetValue(callId, out var s) ? s : null;

    public int GetActiveCallCount() => _activeCalls.Count;
}
