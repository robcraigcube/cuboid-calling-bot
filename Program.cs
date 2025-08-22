using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Polly;
using Polly.Extensions.Http;
using Cuboid.CallingBot.Services;

var builder = WebApplication.CreateBuilder(args);

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// TTS service
builder.Services.AddSingleton<SpeechService>();

// Brain client
var brainUrl = builder.Configuration["BRAIN_URL"];
if (!string.IsNullOrWhiteSpace(brainUrl))
{
    builder.Services.AddHttpClient("Brain", client =>
    {
        client.BaseAddress = new Uri(brainUrl!.TrimEnd('/'));
        client.Timeout = TimeSpan.FromSeconds(20);
    })
    .AddPolicyHandler(GetRetryPolicy());
}

var app = builder.Build();

// Swagger (always on – it’s handy for you)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cuboid.CallingBot v1");
});

app.MapGet("/", () => Results.Ok("Cuboid.CallingBot online")).WithOpenApi();

app.MapGet("/healthz", () => Results.Ok("Healthy")).WithOpenApi();

/// Simple TTS endpoint for direct text -> mp3 test
app.MapGet("/api/tts", async (
    [FromQuery] string text,
    [FromQuery] string? voice,
    SpeechService tts,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(text)) return Results.BadRequest("text required");
    var mp3 = await tts.SynthesizeAsync(text, voice, ct);
    return Results.File(mp3, "audio/mpeg", "tts.mp3");
}).WithOpenApi().Produces<FileContentResult>(StatusCodes.Status200OK, "audio/mpeg");

/// Request contracts (kept tiny & explicit)
public record SayRequest(string? prompt, string? voice, bool useBrain = false);
public record JoinRequest(string? meetingUrl, string? tenantId, string? displayName);
public record BrainResponse(string? speech, string? text);

/// /api/control/say -> if useBrain=true, call the brain first, else speak provided prompt.
app.MapPost("/api/control/say", async (
    [FromBody] SayRequest req,
    IHttpClientFactory http,
    IConfiguration config,
    SpeechService tts,
    CancellationToken ct) =>
{
    var text = req.prompt ?? "";

    if (req.useBrain)
    {
        var client = TryGetBrainClient(http);
        if (client is not null)
        {
            try
            {
                var brainRes = await client.PostAsJsonAsync("/api/brain", new { prompt = text }, ct);
                if (brainRes.IsSuccessStatusCode)
                {
                    var payload = await brainRes.Content.ReadFromJsonAsync<BrainResponse>(cancellationToken: ct);
                    // Accept either "speech" or "text" from the brain
                    text = (payload?.speech ?? payload?.text ?? "").Trim();
                }
                else
                {
                    text = "I'm having trouble getting a response from the brain right now.";
                }
            }
            catch
            {
                text = "I'm having trouble getting a response from the brain right now.";
            }
        }
        else
        {
            text = "Brain URL is not configured.";
        }
    }

    if (string.IsNullOrWhiteSpace(text))
        text = "Hello. This is Cuboid speaking from Azure.";

    var mp3 = await tts.SynthesizeAsync(text, req.voice, ct);
    return Results.File(mp3, "audio/mpeg", "say.mp3");
}).WithOpenApi().Produces<FileContentResult>(StatusCodes.Status200OK, "audio/mpeg");

/// Stub join/leave to keep your current API surface (no-op for now)
app.MapPost("/api/control/join", (JoinRequest _) =>
    Results.Ok(new { joined = true, note = "Join stub — media stack not enabled yet." })
).WithOpenApi();

app.MapPost("/api/control/leave", () =>
    Results.Ok(new { left = true })
).WithOpenApi();

app.Run();

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(r => r.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(200 * Math.Pow(2, i)));

static HttpClient? TryGetBrainClient(IHttpClientFactory factory)
{
    try { return factory.CreateClient("Brain"); }
    catch { return null; }
}
