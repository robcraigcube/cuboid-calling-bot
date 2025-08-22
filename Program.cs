using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Json;
using Cuboid.CallingBot.Services;

var builder = WebApplication.CreateBuilder(args);

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Cuboid.CallingBot", Version = "v1" });
});

// HTTP + our services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<SpeechService>();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Text("Healthy")).WithOpenApi();

if (app.Environment.IsDevelopment() || true)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new
{
    name = "Cuboid.CallingBot",
    version = "v1",
    time = DateTime.UtcNow
})).WithOpenApi();

// ---------- TTS: stream MP3 to caller ----------
app.MapGet("/api/tts", async (string text, string? voice, SpeechService speech) =>
{
    if (string.IsNullOrWhiteSpace(text)) return Results.BadRequest("Query param 'text' is required.");

    var bytes = await speech.SynthesizeAsync(text, voice);
    return Results.File(bytes, "audio/mpeg", $"tts.mp3", enableRangeProcessing: true);
})
.Produces(StatusCodes.Status200OK)
.WithSummary("Text-to-speech (MP3)")
.WithDescription("Streams MP3 synthesized by Azure Speech.")
.WithOpenApi();

// ---------- Control: say ----------
app.MapPost("/api/control/say", async (SayRequest body, IConfiguration cfg, IHttpClientFactory httpFactory, SpeechService speech) =>
{
    string textToSpeak;

    if (!string.IsNullOrWhiteSpace(body.Text))
    {
        textToSpeak = body.Text!;
    }
    else
    {
        // call your brain if Text not provided
        var brainUrl = cfg["BRAIN_URL"];
        if (string.IsNullOrWhiteSpace(brainUrl))
            return Results.BadRequest("Either supply 'text' or set BRAIN_URL in App Settings.");

        try
        {
            var client = httpFactory.CreateClient();
            var payload = JsonSerializer.Serialize(new { prompt = body.Prompt ?? "Say hello from Cuboid." });
            var res = await client.PostAsync(brainUrl, new StringContent(payload, Encoding.UTF8, "application/json"));
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync();
            var brain = JsonSerializer.Deserialize<BrainResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            textToSpeak = string.IsNullOrWhiteSpace(brain?.Speech) ? "I have no response right now." : brain!.Speech!;
        }
        catch (Exception ex)
        {
            textToSpeak = $"I couldn't reach my brain service. ({ex.GetType().Name})";
        }
    }

    // speak it and return the MP3
    var mp3 = await speech.SynthesizeAsync(textToSpeak, body.Voice);
    return Results.File(mp3, "audio/mpeg", "say.mp3", enableRangeProcessing: true);
})
.WithSummary("Speak text or brain response")
.WithDescription("If 'text' omitted, calls BRAIN_URL expecting { speech: string } in response.")
.WithOpenApi();

// ---------- Control: join / leave (stubs for now) ----------
app.MapPost("/api/control/join", (JoinRequest req) =>
{
    // Placeholder — Teams join/bridge comes in next milestone
    return Results.Ok(new { status = "stub", message = $"Would join meeting: {req.MeetingUrl}" });
}).WithOpenApi();

app.MapPost("/api/control/leave", () =>
{
    // Placeholder
    return Results.Ok(new { status = "stub", message = "Would leave the current meeting." });
}).WithOpenApi();

app.Run();

// ---------- request/response models ----------
public record SayRequest(string? Text, string? Prompt, string? Voice);
public record JoinRequest(string MeetingUrl);
public record BrainResponse(string? Speech);
