using System.Net.Http.Headers;
using System.Net.Http.Json;
using Azure.Core;
using Azure.Identity;

namespace Cuboid.CallingBot.Services;

public class GraphCallingClient
{
    private readonly HttpClient _http;
    private readonly ClientSecretCredential _cred;
    private readonly ILogger<GraphCallingClient> _logger;

    private const string GraphBase = "https://graph.microsoft.com/beta";

    public GraphCallingClient(HttpClient http, ILogger<GraphCallingClient> logger)
    {
        _http = http;
        _logger = logger;

        var tenantId = Environment.GetEnvironmentVariable("MS_TENANT_ID") ?? "";
        var clientId = Environment.GetEnvironmentVariable("MS_APP_ID") ?? "";
        var clientSecret = Environment.GetEnvironmentVariable("MS_APP_SECRET") ?? "";

        _cred = new ClientSecretCredential(tenantId, clientId, clientSecret);
    }

    private async Task AuthenticateAsync()
    {
        var token = await _cred.GetTokenAsync(
            new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" }));
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Token);
    }

    public async Task AnswerAsync(string callId, string callbackUri)
    {
        await AuthenticateAsync();

        var url = $"{GraphBase}/communications/calls/{callId}/answer";
        var body = new
        {
            callbackUri = callbackUri,
            acceptedModalities = new[] { "audio" },
            mediaConfig = new Dictionary<string, object>
            {
                { "@odata.type", "#microsoft.graph.appHostedMediaConfig" },
                { "blob", "app-hosted-media-config" }
            }
        };

        var resp = await _http.PostAsJsonAsync(url, body);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Graph answer failed {Status}: {Body}",
                resp.StatusCode, await resp.Content.ReadAsStringAsync());
        }
    }

    public async Task RejectAsync(string callId, string reason = "busy")
    {
        await AuthenticateAsync();

        var url = $"{GraphBase}/communications/calls/{callId}/reject";
        var body = new { reason };

        var resp = await _http.PostAsJsonAsync(url, body);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Graph reject failed {Status}: {Body}",
                resp.StatusCode, await resp.Content.ReadAsStringAsync());
        }
    }

    public async Task HangupAsync(string callId)
    {
        await AuthenticateAsync();

        var url = $"{GraphBase}/communications/calls/{callId}/hangup";
        var resp = await _http.PostAsJsonAsync(url, new { });

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Graph hangup failed {Status}: {Body}",
                resp.StatusCode, await resp.Content.ReadAsStringAsync());
        }
    }
}
