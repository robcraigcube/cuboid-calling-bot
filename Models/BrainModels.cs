using System.Text.Json.Serialization;

namespace Cuboid.CallingBot.Models;

public class BrainRequest
{
    [JsonPropertyName("meetingId")]
    public string MeetingId { get; set; } = string.Empty;

    [JsonPropertyName("speaker")]
    public string Speaker { get; set; } = string.Empty;

    [JsonPropertyName("utterance")]
    public string Utterance { get; set; } = string.Empty;

    [JsonPropertyName("history")]
    public string History { get; set; } = string.Empty;

    [JsonPropertyName("constraints")]
    public object Constraints { get; set; } = new { maxVoiceSecs = 20 };
}

public class BrainResponse
{
    [JsonPropertyName("speech")]
    public string Speech { get; set; } = string.Empty;

    [JsonPropertyName("chat")]
    public string? Chat { get; set; }

    [JsonPropertyName("actions")]
    public List<string> Actions { get; set; } = new List<string>();
}

public class CallbackNotification
{
    [JsonPropertyName("changeType")]
    public string? ChangeType { get; set; }

    [JsonPropertyName("resourceUrl")]
    public string ResourceUrl { get; set; } = string.Empty;

    [JsonPropertyName("resourceData")]
    public object? ResourceData { get; set; }
}

public class CallbackNotificationCollection
{
    [JsonPropertyName("value")]
    public List<CallbackNotification>? Value { get; set; }
}
