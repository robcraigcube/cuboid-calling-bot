using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Minimal API + Swagger only
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Simple health checks so Azure/GitHub runner can verify the app is alive
app.MapGet("/", () => Results.Ok(new { ok = true, service = "Cuboid Control API" }));
app.MapGet("/healthz", () => Results.Ok("ok"));

// A tiny echo endpoint we’ll extend later
app.MapPost("/api/echo", async (HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body);
    var text = await reader.ReadToEndAsync();
    return Results.Ok(new { you_said = text });
});

app.Run();


