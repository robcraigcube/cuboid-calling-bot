using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.CognitiveServices.Speech;

var builder = WebApplication.CreateBuilder(args);

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// HttpClient for brain
builder.Services.AddHttpClient("brain")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = System.Net.DecompressionMethods.All
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// --- Helpers -------------------------------------------------------------

static async Task<byte[]> SpeakAsync(string text, string voice, string speechKey, string speechRegion)
{
    var cfg = SpeechConfig.FromSubscription(speechKey, speechRegion);
    // 16kHz 32kbps MP3 is small+clear; change if you prefer
    cfg.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3);
    cfg.SpeechSynthesisVoiceName = string.IsNullOrWhiteSpace(voice) ? "en-GB-LibbyNeural" : voice;

    using var synthesizer = new SpeechSynthesizer(cfg, null);
    var result = await synthesizer.SpeakTextAsync(text);

    if (result.Reason == ResultReason.SynthesizingAudioCompleted)
        return result.AudioData;

    var reason = result.Reason.ToString();
    var detail = SpeechSynthesisCancellationDetails.FromResult(result);
    throw new InvalidOperationException($"TTS failed: {reason}. {detail.Reason} {detail.ErrorCode} {detail.ErrorDetails}");
}

static bool TryGetString(JsonElement root, out string? text)
{
    // Common top-level keys
    foreach (var key in new[] { "speech", "text", "message", "response", "output", "content", "answer" })
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(key, out var v) &&
            v.ValueKind == JsonValueKind.String)
        {
            text = v.GetString();
            return true;
        }
    }

    // Nested patterns we often see
    if (TryPath(root, out text, "data", "speech")) return true;
    if (TryPath(root, out text, "data", "text")) return true;
    if (TryPath(root, out text, "message", "content")) return true;

    // OpenAI-like: choices[0].message.content
    if (root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("choices", out var choices) &&
        choices.ValueKind == JsonValueKind.Array &&
        choices.GetArrayLength() > 0)
    {
        var c0 = choices[0];
        if (TryPath(c0, out text, "message", "content")) return true;
        if (TryPath(c0, out text, "delta", "content")) return true;
    }

    text = null;
    return false;

    static bool TryPath(JsonElement e, out string? value, params string[] path)
    {
        var cur = e;
        foreach (var seg in path)
        {
            if (cur.ValueKind == JsonValueKind.Object)
            {
                if (!cur.TryGetProperty(seg, out cur))
                {
                    value = null; return false;
                }
            }
            else if (cur.ValueKind == JsonValueKind.Array && int.TryParse(seg, out var idx))
            {
                if (idx >= cur.GetArrayLength()) { value = null; return false; }
                cur = cur[idx];
            }
            else { value = null; return false; }
        }

        if (cur.ValueKind == JsonValueKind.String)
        {
            value = cur.GetString();
            return true;
        }

        value = null;
        return false;
    }
}

static async Task<string?> GetBrainTextAsync(HttpClient http, string brainUrl, string prompt, ILogger log)
{
    var payload = new { prompt };
    using var req = new HttpRequestMessage(HttpMethod.Post, brainUrl)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    };

    var resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead);
    var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
    var body = await resp.Content.ReadAsStringAsync();

    log.LogInformation("Brain status {Status}, CT {CT}, body preview: {Preview}",
        (int)resp.StatusCode, ct, body.Length > 300 ? body[..300] + "…" : body);

    if (!resp.IsSuccessStatusCode) return null;

    // Try JSON
    try
    {
        var doc = JsonDocument.Parse(body);
        if (TryGetString(doc.RootElement, out var text) && !string.IsNullOrWhiteSpace(text))
            return text!.Trim();
    }
    catch
    {
        // Not JSON — fall through
    }

    // If not JSON (or we couldn’t find a string), treat as raw text if it looks like text
    if (ct.StartsWith("text/") || !body.TrimStart().StartsWith("{"))
        return body.Trim();

    return null;
}

// --- Routes --------------------------------------------------------------

app.MapGet("/", () => Results.Text("Cuboid.CallingBot is running. See /swagger\n", "text/plain"));

app.MapGet("/healthz", () => Results.Text("Healthy", "text/plain"));

app.MapGet("/api/tts", async (HttpContext ctx) =>
{
    var text = ctx.Request.Query["text"].ToString();
    var voice = ctx.Request.Query["voice"].ToString();

    var key = Environment.GetEnvironmentVariable("SPEECH_KEY") ?? "";
    var region = Environment.GetEnvironmentVariable("SPEECH_REGION") ?? "";

    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
        return Results.BadRequest("Missing SPEECH_KEY / SPEECH_REGION.");

    if (string.IsNullOrWhiteSpace(text))
        return Results.BadRequest("Query ?text= is required.");

    var audio = await SpeakAsync(text, voice, key, region);
    return Results.File(audio, "audio/mpeg", "tts.mp3");
})
.WithName("TextToSpeech (MP3)");

app.MapPost("/api/control/say", async (
    HttpContext ctx,
    IHttpClientFactory httpFactory,
    ILoggerFactory lf) =>
{
    var log = lf.CreateLogger("say");
    var key = Environment.GetEnvironmentVariable("SPEECH_KEY") ?? "";
    var region = Environment.GetEnvironmentVariable("SPEECH_REGION") ?? "";
    var brainUrl = Environment.GetEnvironmentVariable("BRAIN_URL") ?? "";

    var req = await JsonSerializer.DeserializeAsync<SayRequest>(ctx.Request.Body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new SayRequest();

    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
        return Results.BadRequest("Missing SPEECH_KEY / SPEECH_REGION.");

    var useBrain = req.UseBrain ?? (!string.IsNullOrWhiteSpace(brainUrl)); // default to brain if configured
    string? toSpeak = null;

    if (useBrain && !string.IsNullOrWhiteSpace(brainUrl))
    {
        var prompt = string.IsNullOrWhiteSpace(req.Prompt)
            ? "Please provide a short greeting as a British compliance assistant."
            : req.Prompt!;

        var http = httpFactory.CreateClient("brain");
        toSpeak = await GetBrainTextAsync(http, brainUrl, prompt, log);
    }
    else
    {
        toSpeak = req.Prompt;
    }

    if (string.IsNullOrWhiteSpace(toSpeak))
    {
        log.LogWarning("Brain returned no usable text; using fallback.");
        toSpeak = "Hello. I’m ready to help with British compliance.";
    }

    var audio = await SpeakAsync(toSpeak!, req.Voice ?? "en-GB-LibbyNeural", key, region);
    return Results.File(audio, "audio/mpeg", "say.mp3");
})
.WithName("Say (brain or direct)");

app.Run();

public record SayRequest
{
    public string? Prompt { get; init; }
    public string? Voice { get; init; }
    public bool? UseBrain { get; init; }
}

