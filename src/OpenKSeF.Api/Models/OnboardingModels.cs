namespace OpenKSeF.Api.Models;

public record OnboardingStatusResponse(
    bool IsComplete,
    bool HasTenant,
    bool HasCredential,
    string? FirstTenantId);
