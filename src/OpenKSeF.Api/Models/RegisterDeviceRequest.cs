using System.ComponentModel.DataAnnotations;
using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Api.Models;

public record RegisterDeviceRequest(
    [param: Required]
    [param: StringLength(512)]
    string Token,
    Platform Platform,
    Guid? TenantId);
