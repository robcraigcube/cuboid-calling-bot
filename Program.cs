using Azure.Identity;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Graph;
using Cuboid.CallingBot.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Graph client (client credentials)
builder.Services.AddSingleton<GraphServiceClient>(_ =>
{
    var credential = new ClientSecretCredential(
        Environment.GetEnvironmentVariable("MS_TENANT_ID"),
        Environment.GetEnvironmentVariable("MS_APP_ID"),
        Environment.GetEnvironmentVariable("MS_APP_SECRET")
    );

    return new GraphServiceClient(credential);
});

// Azure Speech
builder.Services.AddSingleton<SpeechConfig>(_ =>
{
    var cfg = SpeechConfig.FromSubscription(
        Environment.GetEnvironmentVariable("SPEECH_KEY"),
        Environment.GetEnvironmentVariable("SPEECH_REGION")
    );
    cfg.SpeechRecognitionLanguage = "en-GB";
    cfg.SpeechSynthesisVoiceName = Environment.GetEnvironmentVariable("TTS_VOICE") ?? "en-GB-LibbyNeural";
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
app.UseAuthorization();
app.MapControllers();

// health
app.MapGet("/healthz", () => "ok");

app.Run();
