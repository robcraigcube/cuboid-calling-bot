using System.Text;
using System.Text.Json;
using Cuboid.CallingBot.Models;

namespace Cuboid.CallingBot.Services;

public class CuboidBrainService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _brainUrl;
    private readonly ILogger<CuboidBrainService> _logger;

    public CuboidBrainService(ILogger<CuboidBrainService> logger)
    {
        _httpClient = new HttpClient();
        _brainUrl = Environment.GetEnvironmentVariable("BRAIN_URL")
                   ?? "https://compliance-ai-robert557.replit.app/llm/respond";
        _logger = logger;
    }

    public async Task<BrainResponse> ProcessUtteranceAsync(
        string meetingId,
        string speaker,
        string utterance,
        string history)
    {
        var request = new BrainRequest
        {
            MeetingId = meetingId,
            Speaker = speaker,
            Utterance = utterance,
            History = history,
            Constraints = new { maxVoiceSecs = 20 }
        };

        try
        {
            _logger.LogInformation("Sending request to brain: {Preview}...",
                Preview(utterance));

            var jsonContent = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_brainUrl, content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var brainResponse = JsonSerializer.Deserialize<BrainResponse>(responseContent)
                               ?? GetFallbackResponse();

            _logger.LogInformation("Brain response received: {Preview}...",
                Preview(brainResponse.Speech));

            return brainResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error communicating with brain service");
            return GetFallbackResponse();
        }
    }

    private static string Preview(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= 50 ? text : text.Substring(0, 50);
    }

    private static BrainResponse GetFallbackResponse() => new()
    {
        Speech = "I'm having trouble connecting to my compliance knowledge right now. " +
                 "Could you repeat that in a moment?",
        Chat = null,
        Actions = new List<string>()
    };

    public void Dispose() => _httpClient.Dispose();
}
