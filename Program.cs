using System.Net.Http.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.CognitiveServices.Speech;

var builder = WebApplication.CreateBuilder(args);

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Replit brain client (always used)
builder.Services.AddHttpClient("brain");

// Build app
var app = builder.Build();

// Swagger UI
app.UseSwagger();
app.UseSwaggerUI();

// Health
app.MapGet("/healthz", () => Results.Ok("Healthy"))
   .WithName("Health")
   .WithOpenApi();

// Root
app.MapGet("/", () => Results.Ok("Cuboid.CallingBot v1"))
   .WithOpenApi();

// Quick TTS test (kept for diagnostics)
app.MapGet("/api/tts", async (HttpContext ctx) =>
{
    var text = ctx.Request.Query["text"].ToString();
    if (string.IsNullOrWhiteSpace(text))
        text = "This is Cuboid speaking from Azure.";

    var bytes = await SynthesizeAsync(text, null);
    return Results.File(bytes, "audio/mpeg", "tts.mp3");
})
.WithOpenApi();

app.MapPost("/api/control/say", async (SayRequest req, IHttpClientFactory httpFactory) =>
{
    // Validate prompt
    var prompt = req.prompt?.Trim();
    if (string.IsNullOrWhiteSpace(prompt))
        return Results.BadRequest(new { error = "prompt is required" });

    // BRAIN_URL must be configured
    var brainUrl = Environment.GetEnvironmentVariable("BRAIN_URL")?.Trim();
    if (string.IsNullOrWhiteSpace(brainUrl))
    {
        var msg = "BRAIN_URL is not configured in App Service settings.";
        var fallback = await SynthesizeAsync("Sorry, my brain is not configured yet.", req.voice);
        // Also return detail as headers for debugging
        return Results.File(fallback, "audio/mpeg", "say.mp3");
    }

    string replyToSpeak;

    try
    {
        // Always call Replit brain
        var http = httpFactory.CreateClient("brain");
        var brainResp = await http.PostAsJsonAsync(brainUrl, new { prompt });

        if (!brainResp.IsSuccessStatusCode)
        {
            // Speak a clear error message (still fully brain-first behavior)
            replyToSpeak = "Sorry, I'm having trouble reaching my brain service.";
        }
        else
        {
            var data = await brainResp.Content.ReadFromJsonAsync<BrainResponse>();

            // Prefer 'speech', otherwise 'text'
            var candidate = (data?.speech ?? data?.text)?.Trim();
            replyToSpeak = !string.IsNullOrWhiteSpace(candidate)
                ? candidate!
                : "Sorry, my brain didn’t return a response.";
        }
    }
    catch
    {
        replyToSpeak = "Sorry, I hit an error contacting my brain.";
    }

    // TTS synth
    var audio = await SynthesizeAsync(replyToSpeak, req.voice);
    return Results.File(audio, "audio/mpeg", "say.mp3");
})
.WithOpenApi()
.WithName("Say");

// Stubs (kept minimal)
app.MapPost("/api/control/join", () => Results.Ok(new { status = "not-implemented" })).WithOpenApi();
app.MapPost("/api/control/leave", () => Results.Ok(new { status = "not-implemented" })).WithOpenApi();

app.Run();

record SayRequest(string? prompt, string? voice);
record BrainResponse(string? speech, string? text);

// ---------- helpers ----------
static async Task<byte[]> SynthesizeAsync(string text, string? voice)
{
    var key = Environment.GetEnvironmentVariable("SPEECH_KEY") ?? "";
    var region = Environment.GetEnvironmentVariable("SPEECH_REGION") ?? "";
    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
        throw new InvalidOperationException("SPEECH_KEY and SPEECH_REGION must be configured in Azure App Service > Configuration.");

    var chosenVoice = string.IsNullOrWhiteSpace(voice)
        ? (Environment.GetEnvironmentVariable("TTS_VOICE")?.Trim() ?? "en-GB-LibbyNeural")
        : voice.Trim();

    var cfg = SpeechConfig.FromSubscription(key, region);
    cfg.SpeechSynthesisVoiceName = chosenVoice;
    cfg.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio24Khz160KBitRateMonoMp3);

    using var synth = new SpeechSynthesizer(cfg, audioConfig: null);
    var result = await synth.SpeakTextAsync(text);

    if (result.Reason == ResultReason.SynthesizingAudioCompleted)
        return result.AudioData;

    if (result.Reason == ResultReason.Canceled)
    {
        var cancel = SpeechSynthesisCancellationDetails.FromResult(result);
        throw new InvalidOperationException($"TTS canceled: {cancel.Reason}; {cancel.ErrorDetails}");
    }

    throw new InvalidOperationException("Unexpected TTS result.");
}
