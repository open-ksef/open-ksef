namespace OpenKSeF.Portal.Tests;

public class PortalWebInfrastructureConfigurationTests
{
    [Fact]
    public void BuildWorkflow_IncludesPortalWebBuildLintAndTestSteps()
    {
        var content = File.ReadAllText(FindFromRepoRoot(".github/workflows/build.yml"));

        Assert.Contains("OpenKSeF.Portal.Web", content, StringComparison.Ordinal);
        Assert.Contains("npm ci", content, StringComparison.Ordinal);
        Assert.Contains("npm run build", content, StringComparison.Ordinal);
        Assert.Contains("npm run lint", content, StringComparison.Ordinal);
        Assert.Contains("npm test", content, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWorkflow_DockerJob_BuildsPortalWebImage()
    {
        var content = File.ReadAllText(FindFromRepoRoot(".github/workflows/build.yml"));

        Assert.Contains("file: ./src/OpenKSeF.Portal.Web/Dockerfile", content, StringComparison.Ordinal);
        Assert.Contains("openksef-portal-web:dev", content, StringComparison.Ordinal);
    }

    [Fact]
    public void DockerCompose_DefinesPortalWebService()
    {
        var content = File.ReadAllText(FindFromRepoRoot("docker-compose.dev.yml"));

        Assert.Contains("portal-web:", content, StringComparison.Ordinal);
        Assert.Contains("dockerfile: OpenKSeF.Portal.Web/Dockerfile", content, StringComparison.Ordinal);
    }

    [Fact]
    public void GatewayConfig_RoutesRootToPortalWebService()
    {
        var content = File.ReadAllText(FindFromRepoRoot("infra/nginx/default.conf"));

        Assert.Contains("set $portal_web_upstream http://portal-web:80;", content, StringComparison.Ordinal);
        Assert.Contains("proxy_pass $portal_web_upstream;", content, StringComparison.Ordinal);
    }

    [Fact]
    public void PortalWebDockerfile_UsesMultiStageNodeAndNginx()
    {
        var content = File.ReadAllText(FindFromRepoRoot("src/OpenKSeF.Portal.Web/Dockerfile"));

        Assert.Contains("FROM node:20-alpine", content, StringComparison.Ordinal);
        Assert.Contains("FROM nginx:alpine", content, StringComparison.Ordinal);
        Assert.Contains("/usr/share/nginx/html", content, StringComparison.Ordinal);
    }

    [Fact]
    public void PortalWebNginxConfig_UsesTryFilesForSpaRouting()
    {
        var content = File.ReadAllText(FindFromRepoRoot("src/OpenKSeF.Portal.Web/nginx.conf"));

        Assert.Contains("try_files $uri $uri/ /index.html;", content, StringComparison.Ordinal);
    }

    private static string FindFromRepoRoot(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath}");
    }
}
