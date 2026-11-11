using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class WorkerStartupTests(TestcontainersFixture fixture)
{
    [Fact]
    public async Task WorkerProcess_Development_StartsOnPostgres()
    {
        var connection = await SeedDatabaseAsync("test");

        var (started, output) = await RunWorkerAsync(connection, envBaseUrl: "test", stopAt: "Application started");

        Assert.True(started, output);
        Assert.Contains("System initialized", output);
    }

    [Theory]
    [InlineData("tset", null)]
    [InlineData(null, "https://ksef.mf.gov.pl/api")]
    public async Task WorkerProcess_UnknownEnvironment_ExitsNonZero_BeforeAnyKSeFCall(string? row, string? envBaseUrl)
    {
        var connection = await SeedDatabaseAsync(row);

        var (_, output) = await RunWorkerAsync(connection, envBaseUrl, stopAt: null);

        Assert.Contains("Unknown KSeF environment", output);
        Assert.DoesNotContain("ksef.mf.gov.pl/v2", output);
    }

    private async Task<string> SeedDatabaseAsync(string? environmentRow)
    {
        var database = $"worker_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(fixture.PostgresConnectionString))
        {
            await admin.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE {database}", admin).ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(fixture.PostgresConnectionString) { Database = database }.ConnectionString;
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
        await db.Database.MigrateAsync();
        db.SystemConfigs.Add(new SystemConfig { Key = SystemConfigKeys.IsInitialized, Value = "true", UpdatedAt = DateTime.UtcNow });
        if (environmentRow is not null)
            db.SystemConfigs.Add(new SystemConfig { Key = SystemConfigKeys.KSeFEnvironment, Value = environmentRow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return connection;
    }

    private static async Task<(bool Stopped, string Output)> RunWorkerAsync(string connection, string? envBaseUrl, string? stopAt)
    {
        var workerDll = Path.Combine(AppContext.BaseDirectory.Replace("OpenKSeF.Api.IntegrationTests", "OpenKSeF.Worker"), "OpenKSeF.Worker.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { workerDll },
            WorkingDirectory = Path.GetDirectoryName(workerDll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment =
            {
                ["DOTNET_ENVIRONMENT"] = "Development",
                ["ConnectionStrings__Db"] = connection,
                ["ENCRYPTION_KEY"] = OpenKSeFWebApplicationFactory.TestEncryptionKey,
                ["KSeF__BaseUrl"] = envBaseUrl ?? "",
                ["KSeF__ForbidProduction"] = "true",
            },
        };

        var output = new StringBuilder();
        var reached = new TaskCompletionSource();
        void OnLine(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null)
                return;
            lock (output)
                output.AppendLine(e.Data);
            if (stopAt is not null && e.Data.Contains(stopAt))
                reached.TrySetResult();
        }

        using var worker = new Process { StartInfo = start };
        worker.OutputDataReceived += OnLine;
        worker.ErrorDataReceived += OnLine;
        worker.Start();
        worker.BeginOutputReadLine();
        worker.BeginErrorReadLine();
        try
        {
            var exited = worker.WaitForExitAsync();
            await Task.WhenAny(exited, reached.Task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (stopAt is null)
            {
                await exited.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.NotEqual(0, worker.ExitCode);
            }
        }
        finally
        {
            worker.Kill(entireProcessTree: true);
        }

        lock (output)
            return (reached.Task.IsCompleted, output.ToString());
    }
}
