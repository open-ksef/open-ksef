namespace OpenKSeF.Domain.Entities;

public class SystemConfig
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public DateTime UpdatedAt { get; set; }
}
