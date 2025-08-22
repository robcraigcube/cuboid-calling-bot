var builder = WebApplication.CreateBuilder(args);

// Minimal API + Swagger (handy while we iterate)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Health endpoint (used by Azure + your checks)
app.MapGet("/healthz", () => Results.Text("ok"));

// Root ping
app.MapGet("/", () => Results.Json(new { name = "Cuboid Control API", status = "running" }));

app.Run();

