using System.Text.Json;
using HeadlessScreenReader.Worker;
using Npgsql;

namespace HeadlessScreenReader.Tests;

public sealed class WorkerHealthTests
{
    [Fact]
    public async Task InputFreeProbeSeparatesUnavailableDependenciesAndLeavesNoEvidenceOrJournal()
    {
        var root = Path.Combine(Path.GetTempPath(), "hsr-health-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = await WorkerHealth.ProbeAsync(root, "sha256:" + new string('a', 64), null, null, default);
            Assert.Equal("hsr-worker-health-v1", result.Schema);
            Assert.Equal(0, result.InputsDispatched);
            Assert.Equal(new[] { new WorkerHealth.Check("DatabaseQueue", "Unavailable"),
                new("ReaderBroker", "Unavailable"), new("PublicProcessor", "Unavailable"),
                new("EvidenceDisk", "Ready"), new("EvidenceStorage", "NotConfigured") }, result.Components);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MissingEvidenceRootIsUnavailableAndNeverCreatedByHealthProbe()
    {
        var root = Path.Combine(Path.GetTempPath(), "hsr-health-" + Guid.NewGuid().ToString("N"));
        var result = await WorkerHealth.ProbeAsync(root, "sha256:" + new string('a', 64), null, null, default);
        Assert.Equal("Unavailable", result.Components.Single(check => check.Component == "EvidenceDisk").State);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("08006", "08006")]
    [InlineData("57P01", "57P01")]
    [InlineData("XX000", null)]
    public void FatalDatabaseDiagnosticsRetainOnlyClosedCategoryAndAllowedSqlState(string code, string? expected)
    {
        var error = new PostgresException("private-page-and-credential", "private-severity", "private-severity", code);
        var result = WorkerStopDiagnostic.From("PublicQueueCycle", error);
        Assert.Equal("hsr-worker-stop-v1", result.Schema);
        Assert.Equal("PublicQueueCycle", result.Stage);
        Assert.Equal("DatabaseRejected", result.Category);
        Assert.Equal(expected, result.SqlState);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(result));
    }

    [Fact]
    public void UnexpectedExceptionMessagesCannotEnterWorkerDiagnostics()
    {
        var result = WorkerStopDiagnostic.From("OwnedRecovery", new InvalidOperationException("private-value"));
        Assert.Equal("Unexpected", result.Category);
        Assert.Null(result.SqlState);
        Assert.False(result.Retryable);
        Assert.DoesNotContain("private-value", JsonSerializer.Serialize(result));
    }
}
