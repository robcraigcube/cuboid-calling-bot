using Azure.Identity;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Graph;
using Cuboid.CallingBot.Services;

var builder = WebApplication.CreateBuilder(args);

// MVC + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// OPTIONAL Graph client (kept for later Calling work; safe if env vars are empty)
builder.Services.AddSingleton<GraphServiceClient>(_ =>
{
    var tenantId = Environment.GetEnvironmentVariable("MS_TENANT_ID");
    var clientId = Environment.GetEnvironmentVariable("MS_APP_ID");
    var clientSecret = Environment.GetEnvironmentVariable("MS_APP_SECRET");

    if (string.IsNullOrWhiteSpace(tenantId) ||
        string.IsNullOrWhiteSpace(clientId) ||
        string.IsNullOrWhiteSpace(clientSecret))
    {
        // Won't be used yet, but returning a client avoids nulls
        return new GraphServiceClient(new DefaultAzureCredential());
    }

    var cred = new ClientSecretCredential(tenantId, clientId, clientSecret);
    return new GraphServiceClient(cred);
});

// Speech (used by AudioProcessingService)
builder.Services.AddSingleton<SpeechConfig>(_ =>
{
    var key = Environment.GetEnvironmentVariable("SPEECH_KEY") ?? "";
    var region = Environment.GetEnvironmentVariable("SPEECH_REGION") ?? "";
    var cfg = SpeechConfig.FromSubscription(key, region);
    cfg.SpeechRecognitionLanguage = "en-GB";
    cfg.SpeechSynthesisVoiceName =
        Environment.GetEnvironmentVariable("TTS_VOICE") ?? "en-GB-LibbyNeural";
    return cfg;
});

// App services
builder.Services.AddSingleton<CuboidCallingService>();
builder.Services.AddSingleton<AudioProcessingService>();
builder.Services.AddSingleton<CuboidBrainService>();
builder.Services.AddSingleton<WakePhraseDetector>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

// Health
app.MapGet("/healthz", () => Results.Text("ok"));

app.Run();
