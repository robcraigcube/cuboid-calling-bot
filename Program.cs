using Microsoft.AspNetCore.Mvc;
using Microsoft.CognitiveServices.Speech;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

// Swagger + minimal API
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// One HttpClient for the brain
builder.Services.AddHttpClient("brain", client =>
{
    var brainUrl = Environment.GetEnvironmentVariable("BRAIN_URL");
    if (!string.IsNullOrWhiteSpace(brainUrl))
    {
        // Accept either full URL with or without trailing slash.
        client.BaseAddress = new Uri(brainUrl);
    }
});

// Azure Speech config (TTS)
builder.Services.AddSingleton(_ =>
{
    var key    = Environment.GetEnvironmentVariable("SPEECH_KEY")    ?? "";
    var region = Environment.GetEnvironmentVariable("SPEECH_REGION") ?? "";
    var cfg    = SpeechConfig.FromSubscription(key, region);

    // British voice by default; override per-request if provided
    cfg.SpeechSynthesisVoiceName =
        Environment.GetEnvironmentVariable("TTS_VOICE") ?? "en-GB-LibbyNeural";

    // A common MP3 format that streams well
    cfg.SetSpeechSynthesisOutputFormat(
        SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3);

    return cfg;
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Ok(new { name = "Cuboid.CallingBot", status = "ok" }));
app.MapGet("/healthz", () => Results.Text("Healthy"));

// Simple TTS test endpoint: GET /api/tts?text=hello&voice=en-GB-LibbyNeural
app.MapGet("/api/tts", async (
    [FromQuery] string text,
    [FromQuery] string? voice,
    [FromServices] SpeechConfig speechCfg) =>
{
    if (!string.IsNullOrWhiteSpace(voice))
        speechCfg.SpeechSynthesisVoiceName = voice;

    using var synth = new SpeechSynthesizer(speechCfg, audioConfig: null);
    var r = await synth.SpeakTextAsync(text);
    if (r.Reason != ResultReason.SynthesizingAudioCompleted)
        return Results.Problem($"TTS failed: {r.Reason}");

    return Results.File(r.AudioData, "audio/mpeg", "say.mp3");
});

// Brain-first “say” that returns an MP3
app.MapPost("/api/control/say", async (
    [FromBody] SayRequest body,
    [FromServices] IHttpClientFactory httpFactory,
    [FromServices] SpeechConfig speechCfg) =>
{
    var finalText = body.Prompt?.Trim();

    // 1) Ask the Replit brain (if configured)
    var brainUrl = Environment.GetEnvironmentVariable("BRAIN_URL");
    if (!string.IsNullOrWhiteSpace(brainUrl))
    {
        try
        {
            using var brain = httpFactory.CreateClient("brain");

            // Allow both full URL or base address:
            var target = brain.BaseAddress?.AbsoluteUri?.EndsWith("/api/brain") == true
                ? ""            // already points at /api/brain
                : "api/brain";  // base is the root -> add path

            var resp = await brain.PostAsJsonAsync(target, new { prompt = body.Prompt });
            if (resp.IsSuccessStatusCode)
            {
                var br = await resp.Content.ReadFromJsonAsync<BrainReply>();
                if (!string.IsNullOrWhiteSpace(br?.speech))
                    finalText = br!.speech!;
            }
        }
        catch
        {
            // If brain is down/slow, we fall back to the provided prompt
        }
    }

    if (string.IsNullOrWhiteSpace(finalText))
        finalText = "Sorry, I couldn't get a response from the brain.";

    // 2) Speak the result
    if (!string.IsNullOrWhiteSpace(body.Voice))
        speechCfg.SpeechSynthesisVoiceName = body.Voice;

    using var synth = new SpeechSynthesizer(speechCfg, audioConfig: null);
    var r = await synth.SpeakTextAsync(finalText);
    if (r.Reason != ResultReason.SynthesizingAudioCompleted)
        return Results.Problem($"TTS failed: {r.Reason}");

    return Results.File(r.AudioData, "audio/mpeg", "say.mp3");
});

// Join/Leave placeholders (kept so Swagger matches what you saw working)
app.MapPost("/api/control/join", (JoinRequest _) => Results.Ok(new { ok = true }));
app.MapPost("/api/control/leave", () => Results.Ok(new { ok = true }));

app.Run();

/// ----- keep types below top-level code (avoids CS8803) -----

record SayRequest(string Prompt, string? Voice);
record JoinRequest(string MeetingId, string TenantId, string? DisplayName);

// matches Replit brain JSON: { "speech": "..." }
sealed class BrainReply { public string? speech { get; set; } }
