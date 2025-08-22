using System.Collections.Concurrent;
using Azure.Communication;
using Azure.Communication.CallAutomation;

namespace Cuboid.CallingBot.Services;

public class AcsCallService
{
    private readonly CallAutomationClient _client;
    private readonly string _baseUrl;
    private readonly ILogger<AcsCallService> _log;

    // callConnectionId -> meetingLink (simple in-memory tracking)
    private readonly ConcurrentDictionary<string, string> _active = new();

    // convenience: last connected call (used if callId omitted on /say)
    private string? _lastConnectedCallId;

    public AcsCallService(IConfiguration cfg, ILogger<AcsCallService> log)
    {
        var cs = cfg["ACS_CONNECTION_STRING"] ?? throw new InvalidOperationException("ACS_CONNECTION_STRING missing");
        _baseUrl = (cfg["PUBLIC_BASE_URL"] ?? throw new InvalidOperationException("PUBLIC_BASE_URL missing")).TrimEnd('/');
        _client = new CallAutomationClient(cs);
        _log = log;
    }

    public async Task<string> JoinTeamsByLinkAsync(string meetingLink, CancellationToken ct = default)
    {
        var callback = new Uri($"{_baseUrl}/api/call/events");

        var create = await _client.CreateCallAsync(
            new TeamsMeetingLinkLocator(meetingLink),
            new CreateCallOptions(
                callSource: new CallSource(new CommunicationUserIdentifier(Guid.NewGuid().ToString())),
                callbackUri: callback
            ),
            ct);

        var callId = create.CallConnection.CallConnectionId;
        _active[callId] = meetingLink;
        _log.LogInformation("CreateCall issued. CallConnectionId={CallId}", callId);
        return callId;
    }

    public async Task SpeakAsync(string text, string? voice, string? callId = null, CancellationToken ct = default)
    {
        var target = callId ?? _lastConnectedCallId
            ?? throw new InvalidOperationException("No active call. Join first or pass callId.");

        var connection = _client.GetCallConnection(target);
        var media = connection.GetCallMedia();

        // Use built-in TTS via TextSource (no storage required)
        var src = new TextSource(text)
        {
            VoiceName = string.IsNullOrWhiteSpace(voice) ? "en-GB-LibbyNeural" : voice
        };

        await media.PlayToAllAsync(src, cancellationToken: ct);
        _log.LogInformation("PlayToAll queued on CallId={CallId}", target);
    }

    public async Task HangupAsync(string? callId = null, CancellationToken ct = default)
    {
        var target = callId ?? _lastConnectedCallId
            ?? throw new InvalidOperationException("No active call. Nothing to hang up.");

        await _client.GetCallConnection(target).HangUpAsync(true, ct);
        _log.LogInformation("HangUp sent. CallId={CallId}", target);
    }

    // Called by events endpoint
    public void MarkConnected(string callId)
    {
        _lastConnectedCallId = callId;
        _log.LogInformation("Call connected. CallId={CallId}", callId);
    }

    public void Remove(string callId)
    {
        _active.TryRemove(callId, out _);
        if (_lastConnectedCallId == callId) _lastConnectedCallId = null;
        _log.LogInformation("Call ended. CallId={CallId}", callId);
    }
}
