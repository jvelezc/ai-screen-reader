using System.Diagnostics;
using System.Text.Json;

namespace HeadlessScreenReader.Tests;

public sealed class PublicReaderLauncherDiagnosticsTests
{
    [Fact]
    public async Task InvalidOwnedLaunchRetainsClosedFailureWithoutContaminatingBinaryChannels()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "public-launch-failure", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "Start-IsolatedPublicReader.ps1"));
        var diagnostic = Path.Combine(root, "launch.json");
        var info = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-Runtime", "podman",
            "-OwnerToken", "invalid-disposable-owner", "-Image", "invalid-unpublished-image", "-FailurePath", diagnostic }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        process.StandardInput.Close(); var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var kill = deadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        await process.WaitForExitAsync(deadline.Token);
        Assert.Equal(3, process.ExitCode); Assert.Empty(await output); Assert.Empty(await errors);
        using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(diagnostic)); var value = json.RootElement;
        Assert.Equal(new[] { "Category", "HResult", "InputTransferState", "NativeExitCode", "Owner", "Schema", "Stage" },
            value.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("hsr-public-reader-launch-failure-v1", value.GetProperty("Schema").GetString());
        Assert.Equal("Isolation", value.GetProperty("Stage").GetString()); Assert.Equal("Runtime", value.GetProperty("Category").GetString());
        Assert.Equal("NotStarted", value.GetProperty("InputTransferState").GetString());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("NativeExitCode").ValueKind); Assert.NotEqual(0, value.GetProperty("HResult").GetInt32());
        Assert.False(File.Exists(diagnostic + ".cleanup.json"));
    }
}
