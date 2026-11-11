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
        try
        {
            var (started, output) = await RunWorkerAsync(connection, envBaseUrl: "test", stopAt: "Application started");

            Assert.True(started, output);
            Assert.Contains("System initialized", output);
            Assert.DoesNotContain("CRT]", output);
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [Theory]
    [InlineData("tset", null)]
    [InlineData(null, "https://ksef.mf.gov.pl/api")]
    public async Task WorkerProcess_UnknownEnvironment_ExitsNonZero_BeforeAnyKSeFCall(string? row, string? envBaseUrl)
    {
        var connection = await SeedDatabaseAsync(row);
        try
        {
            var (_, output) = await RunWorkerAsync(connection, envBaseUrl, stopAt: null);

            Assert.Contains("Unknown KSeF environment", output);
            Assert.DoesNotContain("ksef.mf.gov.pl/v2", output);
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    [Fact]
    public async Task WorkerProcess_KeyRowAndNoKey_ExitsOne_WritesNothing()
    {
        var connection = await SeedDatabaseAsync("test", keyRow: OpenKSeFWebApplicationFactory.TestEncryptionKey);
        try
        {
            var (_, output) = await RunWorkerAsync(connection, envBaseUrl: "test", stopAt: null, encryptionKey: "");

            Assert.Contains("no longer reads the key from the database", output);
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
            Assert.Equal(
                OpenKSeFWebApplicationFactory.TestEncryptionKey,
                (await db.SystemConfigs.SingleAsync(c => c.Key == SystemConfigKeys.EncryptionKey)).Value);
        }
        finally
        {
            await DropDatabaseAsync(connection);
        }
    }

    private async Task<string> SeedDatabaseAsync(string? environmentRow, string? keyRow = null)
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
        if (keyRow is not null)
            db.SystemConfigs.Add(new SystemConfig { Key = SystemConfigKeys.EncryptionKey, Value = keyRow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return connection;
    }

    private async Task DropDatabaseAsync(string connection)
    {
        await using (var own = new NpgsqlConnection(connection))
            NpgsqlConnection.ClearPool(own);
        await using var admin = new NpgsqlConnection(fixture.PostgresConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS {new NpgsqlConnectionStringBuilder(connection).Database} WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }

    private static async Task<(bool Stopped, string Output)> RunWorkerAsync(
        string connection, string? envBaseUrl, string? stopAt, string encryptionKey = OpenKSeFWebApplicationFactory.TestEncryptionKey)
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
                ["ENCRYPTION_KEY"] = encryptionKey,
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
                Assert.Equal(1, worker.ExitCode);
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
