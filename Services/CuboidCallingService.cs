using Microsoft.Graph;
using Microsoft.Graph.Models;
using Cuboid.CallingBot.Models;

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
        _logger.LogInformation($"Processing notification: {notification.ChangeType} for {notification.ResourceUrl}");

        try
        {
            var callId = ExtractCallId(notification.ResourceUrl);
            
            switch (notification.ChangeType?.ToLower())
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
                    _logger.LogInformation($"Unhandled notification type: {notification.ChangeType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing notification for {notification.ResourceUrl}");
        }
    }

    private async Task HandleIncomingCallAsync(string callId)
    {
        try
        {
            _logger.LogInformation($"Handling incoming call: {callId}");

            // Answer the call with application-hosted media
            var answerRequest = new AnswerPostRequestBody
            {
                CallbackUri = "https://cuboid-calling-bot-rwc-axdpaqetgqd4aphz.uksouth-01.azurewebsites.net/api/calling",
                AcceptedModalities = new List<Modality?> { Modality.Audio },
                MediaConfig = new AppHostedMediaConfig
                {
                    Blob = GenerateMediaConfigBlob(),
                    RemoveFromDefaultAudioGroup = false
                }
            };

            await _graphClient.Communications.Calls[callId].Answer.PostAsync(answerRequest);

            // Create and track call session
            var session = new CallSession(callId);
            _activeCalls[callId] = session;

            _logger.LogInformation($"Successfully answered call: {callId}");

            // Start audio processing
            await _audioService.StartAudioProcessingAsync(session);

            // Wait for call to stabilize before announcing
            await Task.Delay(2000);
            
            // Send join announcement
            await SendJoinAnnouncementAsync(callId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error answering call {callId}");
            
            // Try to reject the call if we can't answer it
            try
            {
                await _graphClient.Communications.Calls[callId].Reject.PostAsync(new RejectPostRequestBody 
                { 
                    Reason = RejectReason.Busy 
                });
            }
            catch (Exception rejectEx)
            {
                _logger.LogError(rejectEx, $"Failed to reject call {callId} after answer failure");
            }
        }
    }

    private async Task HandleCallUpdateAsync(string callId, CallbackNotification notification)
    {
        try
        {
            _logger.LogInformation($"Call updated: {callId}");
            
            if (_activeCalls.TryGetValue(callId, out var session))
            {
                // In full implementation, this would handle:
                // - Call state changes (connecting, connected, disconnected)
                // - Participant changes
                // - Media state updates
                // - Audio stream events
                
                _logger.LogDebug($"Processing call update for session {callId}");
            }
            else
            {
                _logger.LogWarning($"Received update for unknown call: {callId}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error handling call update for {callId}");
        }
    }

    private async Task HandleCallEndedAsync(string callId)
    {
        try
        {
            _logger.LogInformation($"Call ended: {callId}");
            
            if (_activeCalls.TryGetValue(callId, out var session))
            {
                session.Dispose();
                _activeCalls.Remove(callId);
                
                _logger.LogInformation($"Cleaned up session for call: {callId}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error handling call end for {callId}");
        }
    }

    private async Task SendJoinAnnouncementAsync(string callId)
    {
        try
        {
            var joinMessage = "Hi all — Cuboid here. It's great to be out of the chat and actually use my voice. I'll stay on mute unless you say 'Cuboid'. If you'd like me to leave or go quiet, just say 'Cuboid, mute'.";
            
            await _audioService.SynthesizeAndPlayAsync(callId, joinMessage);
            
            _logger.LogInformation($"Sent join announcement for call: {callId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error sending join announcement for call {callId}");
        }
    }

    public async Task HangupCallAsync(string callId)
    {
        try
        {
            _logger.LogInformation($"Hanging up call: {callId}");
            
            await _graphClient.Communications.Calls[callId].Hangup.PostAsync();
            
            if (_activeCalls.TryGetValue(callId, out var session))
            {
                session.Dispose();
                _activeCalls.Remove(callId);
            }
            
            _logger.LogInformation($"Successfully hung up call: {callId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error hanging up call {callId}");
        }
    }

    private string ExtractCallId(string resourceUrl)
    {
        try
        {
            // Extract call ID from resource URL like "/communications/calls/{callId}" 
            // or "/app/calls/{callId}"
            var segments = resourceUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].Equals("calls", StringComparison.OrdinalIgnoreCase))
                {
                    return segments[i + 1];
                }
            }
            
            // Fallback: take the last segment
            return segments.LastOrDefault() ?? resourceUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error extracting call ID from resource URL: {resourceUrl}");
            return resourceUrl;
        }
    }

    private string GenerateMediaConfigBlob()
    {
        // In full implementation, this would generate the proper media configuration blob
        // for application-hosted media with Graph Calling
        return "application-hosted-media-config";
    }

    public CallSession? GetCallSession(string callId)
    {
        return _activeCalls.TryGetValue(callId, out var session) ? session : null;
    }

    public int GetActiveCallCount()
    {
        return _activeCalls.Count;
    }
}
