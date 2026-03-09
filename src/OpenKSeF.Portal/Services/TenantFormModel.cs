using System.ComponentModel.DataAnnotations;

namespace OpenKSeF.Portal.Services;

public sealed class TenantFormModel
{
    [Required]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "NIP must contain exactly 10 digits.")]
    public string Nip { get; set; } = string.Empty;

    [StringLength(200)]
    public string? DisplayName { get; set; }

    [EmailAddress]
    [StringLength(320)]
    public string? NotificationEmail { get; set; }
}
