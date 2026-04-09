using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ValidationTests;

public class ValidationResultTests
{
    [Fact]
    public void HasErrors_WhenNoMessages_ReturnsFalse()
    {
        var result = new ValidationResult([]);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void HasErrors_WhenOnlyWarning_ReturnsFalse()
    {
        var msg = new ValidationMessage("INV-VAL-001", ValidationSeverity.Warning,
            ValidationStage.Draft, "User msg", "Tech msg");
        var result = new ValidationResult([msg]);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void HasErrors_WhenError_ReturnsTrue()
    {
        var msg = new ValidationMessage("INV-VAL-001", ValidationSeverity.Error,
            ValidationStage.Approve, "User msg", "Tech msg");
        var result = new ValidationResult([msg]);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void HasErrors_MixedMessages_ReturnsTrueWhenAnyError()
    {
        var warning = new ValidationMessage("INV-VAL-001", ValidationSeverity.Warning,
            ValidationStage.Draft, "Warn", "Warn tech");
        var error = new ValidationMessage("INV-VAL-002", ValidationSeverity.Error,
            ValidationStage.Approve, "Error", "Error tech");
        var result = new ValidationResult([warning, error]);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Empty_HasNoMessages()
    {
        Assert.Empty(ValidationResult.Empty.Messages);
        Assert.False(ValidationResult.Empty.HasErrors);
    }

    [Fact]
    public void ValidationMessage_WithOptionalPath_StoresPath()
    {
        var msg = new ValidationMessage("INV-VAL-001", ValidationSeverity.Error,
            ValidationStage.Approve, "User", "Tech", Path: "Lines[0].UnitPrice");
        Assert.Equal("Lines[0].UnitPrice", msg.Path);
    }

    [Fact]
    public void ValidationMessage_WithoutPath_PathIsNull()
    {
        var msg = new ValidationMessage("INV-VAL-001", ValidationSeverity.Error,
            ValidationStage.Approve, "User", "Tech");
        Assert.Null(msg.Path);
    }

    [Fact]
    public void ValidationContext_HoldsAllFields()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var now = DateTime.UtcNow;
        var policies = TestPolicySnapshot.Default;
        var items = new Dictionary<string, object?> { ["key"] = "val" };

        var ctx = new ValidationContext(
            ValidationStage.Approve,
            tenantId,
            now,
            policies,
            IsKsefSubmissionRequested: true,
            IsNumberAssigned: false,
            Items: items);

        Assert.Equal(ValidationStage.Approve, ctx.Stage);
        Assert.Equal(tenantId, ctx.TenantId);
        Assert.Equal(now, ctx.Now);
        Assert.Same(policies, ctx.Policies);
        Assert.True(ctx.IsKsefSubmissionRequested);
        Assert.False(ctx.IsNumberAssigned);
        Assert.Equal("val", ctx.Items["key"]);
    }
}
