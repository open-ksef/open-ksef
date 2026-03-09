namespace OpenKSeF.Portal.Services;

public sealed class TenantOperationResult
{
    private TenantOperationResult(bool success, string? errorMessage)
    {
        Success = success;
        ErrorMessage = errorMessage;
    }

    public bool Success { get; }
    public string? ErrorMessage { get; }

    public static TenantOperationResult Ok() => new(success: true, errorMessage: null);

    public static TenantOperationResult Fail(string errorMessage) => new(success: false, errorMessage);
}
