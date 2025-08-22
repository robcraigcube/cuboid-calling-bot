using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// --- Services ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin()
     .AllowAnyHeader()
     .AllowAnyMethod()
));

// --- App pipeline ---
var app = builder.Build();

app.UseCors();
app.UseRouting();

// Always enable Swagger (also in Production/Azure)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cuboid Calling Bot API v1");
    c.RoutePrefix = "swagger"; // => /swagger
});

// Health
app.MapHealthChecks("/healthz");

// Simple root
app.MapGet("/", () => Results.Ok(new { name = "Cuboid.CallingBot", status = "running" }));

// --- Control API (placeholders to be wired to Teams later) ---
var control = app.MapGroup("/api/control");

control.MapPost("/join", (JoinRequest req) =>
{
    // TODO: integrate Microsoft Graph Calling join
    return Results.Ok(new { accepted = true, meeting = req.MeetingUrl });
})
.WithName("JoinMeeting")
.WithOpenApi();

control.MapPost("/leave", () =>
{
    // TODO: hang up call
    return Results.Ok(new { ok = true });
})
.WithName("LeaveMeeting")
.WithOpenApi();

control.MapPost("/say", (SayRequest req) =>
{
    // TODO: send to Replit brain + TTS playback
    return Results.Ok(new { queued = true, text = req.Text });
})
.WithName("SayInMeeting")
.WithOpenApi();

app.Run();

// --- DTOs ---
public record JoinRequest(string MeetingUrl);
public record SayRequest(string Text);

