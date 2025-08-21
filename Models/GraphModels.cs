namespace Cuboid.CallingBot.Models
{
    // Minimal, SDK-free stand-ins so the project compiles
    public enum Modality { Audio }

    public enum RejectReason
    {
        None = 0,
        Busy = 1,
        Forbidden = 2
    }

    public sealed class AppHostedMediaConfig
    {
        public string Blob { get; set; } = string.Empty;
        public bool RemoveFromDefaultAudioGroup { get; set; } = false;
    }

    public sealed class AnswerPostRequestBody
    {
        public string CallbackUri { get; set; } = string.Empty;
        public List<Modality?> AcceptedModalities { get; set; } = new();
        public AppHostedMediaConfig MediaConfig { get; set; } = new();
    }

    public sealed class RejectPostRequestBody
    {
        public RejectReason Reason { get; set; } = RejectReason.None;
    }
}
