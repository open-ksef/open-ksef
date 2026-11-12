using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Writers;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace OpenKSeF.Api.IntegrationTests;

/// <summary>Fails when the generated OpenAPI document drifts from the committed openapi/v1.json; UPDATE_OPENAPI=1 rewrites the file.</summary>
[Collection(TestcontainersCollection.Name)]
public class OpenApiContractTests(TestcontainersFixture fixture)
{
    [Fact]
    public void GeneratedOpenApi_MatchesCommittedFile()
    {
        using var factory = new OpenKSeFWebApplicationFactory(fixture);
        var document = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        using var writer = new StringWriter();
        document.SerializeAsV3(new OpenApiJsonWriter(writer));
        var actual = Sorted(JsonNode.Parse(writer.ToString()))!.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";

        var path = Path.Combine(RepoRoot(), "openapi", "v1.json");
        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        var expected = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : "";
        if (expected == actual)
            return;

        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var line = Enumerable.Range(0, Math.Max(expectedLines.Length, actualLines.Length))
            .First(i => i >= expectedLines.Length || i >= actualLines.Length || expectedLines[i] != actualLines[i]);
        Assert.Fail($"""
            openapi/v1.json does not match the API. First difference at line {line + 1}:
              committed: {(line < expectedLines.Length ? expectedLines[line].Trim() : "<end of file>")}
              generated: {(line < actualLines.Length ? actualLines[line].Trim() : "<end of file>")}
            Regenerate it (Docker required), commit it, and list portal types.ts and mobile Models impact in the PR:
              UPDATE_OPENAPI=1 dotnet test src/OpenKSeF.Api.IntegrationTests --filter OpenApiContractTests
            """);
    }

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray array => new JsonArray(array.Select(Sorted).ToArray()),
        _ => node?.DeepClone(),
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "src", "OpenKSeF.sln")))
            dir = dir.Parent ?? throw new DirectoryNotFoundException("src/OpenKSeF.sln not found above " + AppContext.BaseDirectory);
        return dir.FullName;
    }
}
