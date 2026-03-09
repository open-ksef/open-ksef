namespace OpenKSeF.Domain.Models;

public class PushNotification
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public Dictionary<string, string> Data { get; init; } = new();
}
