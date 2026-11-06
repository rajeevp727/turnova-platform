namespace TurnOva.API;

public sealed class AiAssistantOptions
{
    public const string SectionName = "AiAssistant";

    public bool Enabled { get; set; }
    public string Provider { get; set; } = "placeholder";
    public string Model { get; set; } = "placeholder";
    public int TimeoutSeconds { get; set; } = 5;
    public int MaxInputCharacters { get; set; } = 1000;
}
