using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// TODO: wire DI for real call/media handling, ASR/TTS, and brain client.
// builder.Services.AddSingleton<ICallManager, CallManager>();
// builder.Services.AddHttpClient<IBrainClient, BrainClient>();

var app = builder.Build();

// health check
app.MapGet("/healthz", () => Results.Ok("ok"));

// Teams Calling webhook (replace stub logic with Graph Calling + media)
app.MapPost("/api/calling", async ([FromBody] object body, HttpRequest req) =>
{
    Console.WriteLine("Received /api/calling event");
    return Results.Ok();
});

// admin endpoint to hang up a stuck call (protect with a token in production)
app.MapPost("/admin/hangup", async ([FromBody] dynamic payload) =>
{
    Console.WriteLine("Hangup requested");
    return Results.Ok();
});

app.Run();
