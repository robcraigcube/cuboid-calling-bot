using Azure.Identity;
using Microsoft.CognitiveServices.Speech;
using Cuboid.CallingBot.Services;

var builder = WebApplication.CreateBuilder(args);

// Controllers & Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// HttpClient for Graph calling
builder.Services.AddHttpClient<GraphCallingClient>();

// Speech
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
app.MapGet("/healthz", () => "ok");

app.Run();
