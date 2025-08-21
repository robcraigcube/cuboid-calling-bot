using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Cuboid.CallingBot.Models;

namespace Cuboid.CallingBot.Services;

public class AudioProcessingService
{
    private readonly SpeechConfig _speechConfig;
    private readonly CuboidBrainService _brainService;
    private readonly WakePhraseDetector _wakePhraseDetector;
    private readonly ILogger<AudioProcessingService> _logger;

    public AudioProcessingService(
        SpeechConfig speechConfig,
        CuboidBrainService brainService,
        WakePhraseDetector wakePhraseDetector,
        ILogger<AudioProcessingService> logger)
    {
        _speechConfig = speechConfig;
        _brainService = brainService;
        _wakePhraseDetector = wakePhraseDetector;
        _logger = logger;
    }

    public async Task StartAudioProcessingAsync(CallSession session)
    {
        try
        {
            _logger.LogInformation($"Starting audio processing for call: {session.CallId}");
            
            // In the full implementation, this would setup real audio streams from Graph Calling
            // For now, we mark the session as audio-ready
            session.IsAudioActive = true;
            
            _logger.LogInformation($"Audio processing started for call: {session.CallId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error starting audio processing for call {session.CallId}");
        }
    }

    public async Task ProcessRecognizedSpeechAsync(CallSession session, string recognizedText)
    {
        try
        {
            if (session.IsMuted)
            {
                _logger.LogDebug($"Call {session.CallId} is muted, ignoring speech: {recognizedText}");
                return;
            }

            var (detected, utterance) = _wakePhraseDetector.ProcessSpeech(recognizedText);
            
            if (detected)
            {
                _logger.LogInformation($"Wake phrase detected in call {session.CallId}: '{utterance}'");

                // Stop any current playback (barge-in support)
                session.StopCurrentPlayback();

                // Check for voice commands first
                if (await HandleVoiceCommandAsync(session, utterance))
                    return;

                // Send to brain for processing
                var brainResponse = await _brainService.ProcessUtteranceAsync(
                    session.CallId,
                    "Unknown Speaker",
                    utterance,
                    session.GetConversationHistory());

                // Play the response
                if (!string.IsNullOrEmpty(brainResponse.Speech))
                {
                    await SynthesizeAndPlayAsync(session.CallId, brainResponse.Speech);
                }

                // Add to conversation history
                session.ConversationHistory.Add($"User: {utterance}");
                session.ConversationHistory.Add($"Cuboid: {brainResponse.Speech}");

                // Keep history manageable
                if (session.ConversationHistory.Count > 20)
                {
                    session.ConversationHistory.RemoveRange(0, 10);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing speech for call {session.CallId}");
        }
    }

    private async Task<bool> HandleVoiceCommandAsync(CallSession session, string utterance)
    {
        var command = utterance.ToLower().Trim();

        try
        {
            switch (command)
            {
                case "mute":
                    session.IsMuted = true;
                    await SynthesizeAndPlayAsync(session.CallId, "I'm now muted. Say 'Cuboid, unmute' to reactivate me.");
                    _logger.LogInformation($"Call {session.CallId} muted via voice command");
                    return true;

                case "unmute":
                    session.IsMuted = false;
                    await SynthesizeAndPlayAsync(session.CallId, "I'm back and listening for your questions.");
                    _logger.LogInformation($"Call {session.CallId} unmuted via voice command");
                    return true;

                case "leave":
                    await SynthesizeAndPlayAsync(session.CallId, "Thanks everyone, I'll leave you to it. Have a productive meeting!");
                    _logger.LogInformation($"Call {session.CallId} leaving via voice command");
                    
                    // Wait for goodbye message to complete, then signal the calling service to hangup
                    await Task.Delay(3000);
                    return true;

                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error handling voice command '{command}' for call {session.CallId}");
            return false;
        }
    }

    public async Task SynthesizeAndPlayAsync(string callId, string text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            _logger.LogInformation($"Synthesizing speech for call {callId}: {text.Substring(0, Math.Min(50, text.Length))}...");

            using var synthesizer = new SpeechSynthesizer(_speechConfig);
            var result = await synthesizer.SpeakTextAsync(text);

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                _logger.LogInformation($"Speech synthesis completed for call {callId}, audio length: {result.AudioData?.Length ?? 0} bytes");
                
                // In full implementation, this would stream the audio data to the Graph Calling media session
                // For now, we log that synthesis was successful
                await StreamAudioToCallAsync(callId, result.AudioData);
            }
            else
            {
                _logger.LogWarning($"Speech synthesis failed for call {callId}: {result.Reason}");
                if (result.Reason == ResultReason.Canceled)
                {
                    var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                    _logger.LogError($"Speech synthesis cancelled: {cancellation.Reason}, {cancellation.ErrorDetails}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error synthesizing speech for call {callId}");
        }
    }

    private async Task StreamAudioToCallAsync(string callId, byte[]? audioData)
    {
        try
        {
            if (audioData == null || audioData.Length == 0)
            {
                _logger.LogWarning($"No audio data to stream for call {callId}");
                return;
            }

            // In full Microsoft Graph Calling implementation, this would:
            // 1. Convert the audio data to the correct format (16kHz mono PCM)
            // 2. Stream it to the call's audio output via the Graph Calling Media SDK
            // 3. Handle audio buffering and timing

            _logger.LogInformation($"Would stream {audioData.Length} bytes of audio to call {callId}");
            
            // Simulate audio playback time
            var estimatedDurationMs = (audioData.Length / 32) * 1000 / 16000; // Rough estimate for 16kHz mono
            await Task.Delay(Math.Min(estimatedDurationMs, 20000)); // Cap at 20 seconds
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error streaming audio to call {callId}");
        }
    }
}
