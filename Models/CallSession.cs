using Microsoft.CognitiveServices.Speech;

namespace Cuboid.CallingBot.Models;

public class CallSession : IDisposable
{
    public string CallId { get; set; }
    public DateTime StartTime { get; set; }
    public bool IsMuted { get; set; }
    public bool IsAudioActive { get; set; }
    public List<string> ConversationHistory { get; set; }
    public SpeechRecognizer? SpeechRecognizer { get; set; }
    private bool _disposed = false;

    public CallSession(string callId)
    {
        CallId = callId;
        StartTime = DateTime.UtcNow;
        IsMuted = false;
        IsAudioActive = false;
        ConversationHistory = new List<string>();
    }

    public string GetConversationHistory()
    {
        return string.Join("\n", ConversationHistory.TakeLast(5));
    }

    public void StopCurrentPlayback()
    {
        // Implementation for stopping current audio playback
        // This would interrupt any ongoing TTS synthesis
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            SpeechRecognizer?.Dispose();
            _disposed = true;
        }
    }
}
