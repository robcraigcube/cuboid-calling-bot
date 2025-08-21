namespace Cuboid.CallingBot.Services;

public class WakePhraseDetector
{
    private readonly string _wakePhrase;
    private readonly ILogger<WakePhraseDetector> _logger;

    public WakePhraseDetector(ILogger<WakePhraseDetector> logger)
    {
        _wakePhrase = Environment.GetEnvironmentVariable("WAKE_PHRASE") ?? "cuboid";
        _logger = logger;
    }

    public (bool detected, string utterance) ProcessSpeech(string recognizedText)
    {
        if (string.IsNullOrWhiteSpace(recognizedText))
            return (false, string.Empty);

        var text = recognizedText.ToLower().Trim();
        var wakePhraseIndex = text.IndexOf(_wakePhrase.ToLower());

        if (wakePhraseIndex == -1)
            return (false, string.Empty);

        _logger.LogInformation($"Wake phrase '{_wakePhrase}' detected in: {recognizedText}");

        // Extract utterance after wake phrase
        var afterWakePhrase = text.Substring(wakePhraseIndex + _wakePhrase.Length).Trim();
        
        // Remove common filler words and punctuation
        afterWakePhrase = afterWakePhrase.TrimStart(',', ' ', '.');
        
        return (true, afterWakePhrase);
    }

    public bool IsCommand(string utterance)
    {
        var command = utterance.ToLower().Trim();
        return command == "mute" || command == "unmute" || command == "leave";
    }
}
