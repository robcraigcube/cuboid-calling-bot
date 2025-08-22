using System.Net.Http.Headers;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Cuboid Control API", Version = "v1" });
});

// HTTP client to talk to the media worker (we’ll set WORKER_BASE_URL later)
builder.Services.AddHttpClient("worker", c =>
{
    var baseUrl = builder.Configuration["WORKER_BASE_URL"] ?? Environment.GetEnvironmentVariable("WORKER_BASE_URL") ?? "";
    if (!string.IsNullOrWhiteSpace(baseUrl) && !baseUrl.EndsWith("/")) baseUrl += "/";
    c.BaseAddress = string.IsNullOrWhiteSpace(baseUrl) ? null : new Uri(baseUrl);
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
})
.AddPolicyHandler(Polly.Extensions.Http.HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(250 * i)));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health
app.MapGet("/healthz", () => Results.Ok("ok"));

// Control endpoints (proxy to the worker)
app.MapPost("/join", async (JoinRequest req, IHttpClientFactory factory) =>
{
    if (string.IsNullOrWhiteSpace(req.JoinUrl))
        return Results.BadRequest("JoinUrl required");

    var client = factory.CreateClient("worker");
    if (client.BaseAddress is null)
        return Results.BadRequest("WORKER_BASE_URL not set on Control API");

    var res = await client.PostAsJsonAsync("api/join", req);
    var content = await res.Content.ReadAsStringAsync();
    return Results.StatusCode((int)res.StatusCode, content);
});

app.MapPost("/leave", async (LeaveRequest req, IHttpClientFactory factory) =>
{
    var client = factory.CreateClient("worker");
    if (client.BaseAddress is null)
        return Results.BadRequest("WORKER_BASE_URL not set on Control API");

    var res = await client.PostAsJsonAsync("api/leave", req);
    var content = await res.Content.ReadAsStringAsync();
    return Results.StatusCode((int)res.StatusCode, content);
});

app.MapGet("/status", async (IHttpClientFactory factory) =>
{
    var client = factory.CreateClient("worker");
    if (client.BaseAddress is null)
        return Results.BadRequest("WORKER_BASE_URL not set on Control API");

    var res = await client.GetAsync("api/status");
    var content = await res.Content.ReadAsStringAsync();
    return Results.StatusCode((int)res.StatusCode, content);
});

app.Run();

record JoinRequest(string JoinUrl, string? ThreadId = null, string? MeetingId = null);
record LeaveRequest(string? CallId = null);
