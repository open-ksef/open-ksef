using System.Text.RegularExpressions;

namespace OpenKSeF.Api.Tests.Architecture;

public class ControllerAggregateMutationGuardTests
{
    private static readonly string ControllersDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OpenKSeF.Api", "Controllers"));

    [Fact]
    public void Controllers_DoNotDirectlyMutateInvoiceAggregate()
    {
        var controllerFiles = Directory.GetFiles(ControllersDirectory, "*Controller*.cs", SearchOption.TopDirectoryOnly);

        Assert.NotEmpty(controllerFiles);

        var violations = new List<string>();

        foreach (var file in controllerFiles)
        {
            var content = File.ReadAllText(file);

            var referencesInvoiceAggregate =
                content.Contains("OpenKSeF.Invoices.Domain.Aggregates", StringComparison.Ordinal) ||
                Regex.IsMatch(content, @"\bInvoice\s+\w+\s*=");

            var mutatesAggregate =
                content.Contains(".Approve(", StringComparison.Ordinal) ||
                content.Contains(".Reopen(", StringComparison.Ordinal) ||
                content.Contains(".SubmitToKsef(", StringComparison.Ordinal) ||
                content.Contains(".AcceptByKsef(", StringComparison.Ordinal) ||
                content.Contains(".RejectByKsef(", StringComparison.Ordinal) ||
                content.Contains(".SetIssueDates(", StringComparison.Ordinal) ||
                content.Contains(".SetCommercialData(", StringComparison.Ordinal) ||
                content.Contains(".SetDocumentNumber(", StringComparison.Ordinal) ||
                content.Contains(".AddLine(", StringComparison.Ordinal);

            if (referencesInvoiceAggregate && mutatesAggregate)
            {
                violations.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Controllers must not mutate OpenKSeF.Invoices.Domain.Aggregates.Invoice directly. Violations: {string.Join(", ", violations)}");
    }
}
