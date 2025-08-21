using Azure.Identity;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Graph;
using Cuboid.CallingBot.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Configure Graph Service Client
builder.Services.AddSingleton<GraphServiceClient>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        var tenantId = Environment.GetEnvironmentVariable("MS_TENANT_ID");
        var clientId = Environment.GetEnvironmentVariable("MS_APP_ID");
        var clientSecret = Environment.GetEnvironmentVariable("MS_APP_SECRET");

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException("Missing required environment variables: MS_TENANT_ID, MS_APP_ID, MS_APP_SECRET");
        }

        var clientSecretCredential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        var graphClient = new GraphServiceClient(clientSecretCredential);
        
        logger.LogInformation("Graph Service Client configured successfully");
        return graphClient;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to configure Graph Service Client");
        throw;
    }
});

// Configure Speech Service
builder.Services.AddSingleton<SpeechConfig>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        var speechKey = Environment.GetEnvironmentVariable("SPEECH_KEY");
        var speechRegion = Environment.GetEnvironmentVariable("SPEECH_REGION");
        var ttsVoice = Environment.GetEnvironmentVariable("TTS_VOICE") ?? "en-GB-LibbyNeural";

        if (string.IsNullOrEmpty(speechKey) || string.IsNullOrEmpty(speechRegion))
        {
            throw new InvalidOperationException("Missing required environment variables: SPEECH_KEY, SPEECH_REGION");
        }

        var config = SpeechConfig.FromSubscription(speechKey, speechRegion);
        config.SpeechRecognitionLanguage = "en-GB";
        config.SpeechSynthesisVoiceName = ttsVoice;
        
        logger.LogInformation($"Speech Service configured: {speechRegion}, Voice: {ttsVoice}");
        return config;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to configure Speech Service");
        throw;
    }
});

// Register application services
builder.Services.AddSingleton<WakePhraseDetector>();
builder.Services.AddSingleton<CuboidBrainService>();
builder.Services.AddSingleton<AudioProcessingService>();
builder.Services.AddSingleton<CuboidCallingService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();

// Map controllers
app.MapControllers();

// Health check endpoint
app.MapGet("/healthz", () => 
{
    return Results.Ok("ok");
}).WithName("HealthCheck");

// Log startup information
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Cuboid Calling Bot starting up...");
logger.LogInformation($"Environment: {app.Environment.EnvironmentName}");
logger.LogInformation($"Wake Phrase: {Environment.GetEnvironmentVariable("WAKE_PHRASE") ?? "cuboid"}");
logger.LogInformation($"Brain URL: {Environment.GetEnvironmentVariable("BRAIN_URL") ?? "https://compliance-ai-robert557.replit.app/llm/respond"}");

app.Run();
