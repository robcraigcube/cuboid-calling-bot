using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace Cuboid.CallingBot.Services;

public sealed class SpeechService
{
    private readonly SpeechConfig _baseConfig;
    private readonly string _defaultVoice;

    public SpeechService(IConfiguration config)
    {
        var key = config["SPEECH_KEY"] ?? "";
        var region = config["SPEECH_REGION"] ?? "";
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
            throw new InvalidOperationException("SPEECH_KEY and SPEECH_REGION must be set in App Service Configuration.");

        _defaultVoice = config["TTS_VOICE"] ?? "en-GB-LibbyNeural";

        _baseConfig = SpeechConfig.FromSubscription(key, region);
        // MP3 output
        _baseConfig.SetSpeechSynthesisOutputFormat(
            SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3);
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, CancellationToken ct = default)
    {
        var cfg = _baseConfig.Clone();
        cfg.SpeechSynthesisVoiceName = string.IsNullOrWhiteSpace(voice) ? _defaultVoice : voice!;

        using var pull = AudioOutputStream.CreatePullStream();
        using var audioCfg = AudioConfig.FromStreamOutput(pull);
        using var synth = new SpeechSynthesizer(cfg, audioCfg);

        var result = await synth.SpeakTextAsync(text);
        if (result.Reason != ResultReason.SynthesizingAudioCompleted)
            throw new InvalidOperationException($"TTS failed: {result.Reason} {result.ErrorDetails}");

        using var stream = AudioDataStream.FromResult(result);
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        uint read;
        while ((read = stream.ReadData(buffer)) > 0)
            ms.Write(buffer, 0, (int)read);
        return ms.ToArray();
    }
}
