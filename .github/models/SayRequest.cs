public sealed class SayRequest
{
    public string? Prompt { get; set; }
    public string? Voice  { get; set; } = "en-GB-LibbyNeural";
    public bool?  UseBrain { get; set; } = true;
}
