using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Cuboid.CallingBot", Version = "v1" });
});

// Services
builder.Services.AddSingleton<SpeechService>();
builder.Services.AddHttpClient("brain");

// App
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Ok("Cuboid Calling Bot is up."));
app.MapGet("/healthz", () => Results.Text("Healthy", "text/plain"));

// Simple TTS helper (no brain)
app.MapGet("/api/tts", async Task<IResult> (string text, string? voice, SpeechService tts) =>
{
    if (string.IsNullOrWhiteSpace(text)) return Results.BadRequest("text is required");
    var mp3 = await tts.SpeakAsync(text, voice);
    return Results.File(mp3, "audio/mpeg", "say.mp3");
})
.WithName("Tts")
.Produces(200);

// Brain → TTS (or direct TTS if you pass a concrete prompt and useBrain=false)
app.MapPost("/api/control/say", async Task<IResult> (SayRequest req, IHttpClientFactory hf, SpeechService tts) =>
{
    string? text = null;

    var useBrain = req.UseBrain ?? true; // default: use brain
    if (useBrain)
    {
        var brainUrl = Environment.GetEnvironmentVariable("BRAIN_URL");
        if (string.IsNullOrWhiteSpace(brainUrl))
            return Results.BadRequest("BRAIN_URL not configured in App Service settings.");

        var http = hf.CreateClient("brain");
        using var resp = await http.PostAsJsonAsync(brainUrl, new { prompt = req.Prompt ?? "In one sentence, introduce yourself as a British compliance assistant." });
        if (!resp.IsSuccessStatusCode)
            return Results.BadRequest($"Brain call failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");

        var brain = await resp.Content.ReadFromJsonAsync<BrainResponse>();
        text = brain?.speech;
        if (string.IsNullOrWhiteSpace(text))
            return Results.BadRequest("Brain responded but without a 'speech' field.");
    }
    else
    {
        if (string.IsNullOrWhiteSpace(req.Prompt))
            return Results.BadRequest("prompt is required when useBrain=false");
        text = req.Prompt;
    }

    var mp3 = await tts.SpeakAsync(text!, req.Voice);
    return Results.File(mp3, "audio/mpeg", "say.mp3");
})
.WithName("Say")
.Produces(200);

// (Placeholders so your existing Swagger shape stays the same)
// Join/Leave endpoints can be filled with Graph Calling later.
app.MapPost("/api/control/join", (JoinRequest req) => Results.Ok(new { ok = true }))
   .WithName("Join");
app.MapPost("/api/control/leave", () => Results.Ok(new { ok = true }))
   .WithName("Leave");

app.Run();
