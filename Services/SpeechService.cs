using System.Net.Http.Headers;
using System.Text;

namespace Cuboid.CallingBot.Services;

public class SpeechService
{
    private readonly HttpClient _http;
    private readonly string _speechKey;
    private readonly string _region;
    private readonly string _defaultVoice;

    public SpeechService(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _http = httpFactory.CreateClient();
        _speechKey = config["SPEECH_KEY"] ?? "";
        _region = config["SPEECH_REGION"] ?? "";
        _defaultVoice = config["TTS_VOICE"] ?? "en-GB-LibbyNeural";
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null)
    {
        if (string.IsNullOrWhiteSpace(_speechKey) || string.IsNullOrWhiteSpace(_region))
            throw new InvalidOperationException("SPEECH_KEY and SPEECH_REGION must be set in App Service configuration.");

        var v = string.IsNullOrWhiteSpace(voice) ? _defaultVoice : voice!;
        var endpoint = $"https://{_region}.tts.speech.microsoft.com/cognitiveservices/v1";

        // SSML so we can pick voice
        var ssml = $@"
<speak version='1.0' xml:lang='en-GB'>
  <voice name='{v}'>{System.Security.SecurityElement.Escape(text)}</voice>
</speak>";

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Add("Ocp-Apim-Subscription-Key", _speechKey);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("CuboidCallingBot", "1.0"));
        req.Headers.Add("X-Microsoft-OutputFormat", "audio-48khz-192kbitrate-mono-mp3");
        req.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");

        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsByteArrayAsync();
    }
}
