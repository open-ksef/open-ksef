namespace OpenKSeF.Domain.Entities;

/// <summary>Last seen identity claims of a user, so other members see a name instead of a Keycloak id.</summary>
public class UserProfile
{
    public required string UserId { get; set; }
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }
    public string? DisplayName { get; set; }
    public DateTime UpdatedAt { get; set; }
}
