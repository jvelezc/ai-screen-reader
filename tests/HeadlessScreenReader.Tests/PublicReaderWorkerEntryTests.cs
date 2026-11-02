using HeadlessScreenReader.Cli;
using System.Text.Json;

namespace HeadlessScreenReader.Tests;

public sealed class PublicReaderWorkerEntryTests
{
    [Theory]
    [InlineData("docker --privileged")]
    [InlineData("docker")]
    public async Task ProofRejectsUnsafeRuntimeOrMissingLauncherWithoutClaimingAcceptance(string runtime)
    {
        using var output = new StringWriter();
        var code = await PublicReaderWorkerProof.RunAsync(runtime, Path.Combine(AppContext.BaseDirectory, "private-path-canary", "missing-repository"), output);
        using var receipt = JsonDocument.Parse(output.ToString());
        Assert.Equal(3, code); Assert.False(receipt.RootElement.GetProperty("Ready").GetBoolean());
        Assert.Equal("Configure", receipt.RootElement.GetProperty("Phase").GetString());
        Assert.Equal("PUBLIC_READER_WORKER_PROOF_UNCONFIRMED", receipt.RootElement.GetProperty("Code").GetString());
        Assert.True(Guid.TryParseExact(receipt.RootElement.GetProperty("AcceptanceRun").GetString(), "N", out _));
        Assert.DoesNotContain("private-path-canary", output.ToString());
        Assert.DoesNotContain("privileged", output.ToString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("ip")]
    [InlineData("loopback")]
    [InlineData("any")]
    [InlineData("ipv6")]
    [InlineData("host")]
    [InlineData("owner")]
    [InlineData("empty-owner")]
    [InlineData("uppercase-owner")]
    [InlineData("target")]
    public async Task BadConfigurationCannotReadInputOrRevealValues(string scenario)
    {
        string[] args = ["10.0.0.5", "screenreader.privensic.com", "abcdefabcdefabcdefabcdefabcdefab", "https://screenreader.privensic.com/ci/v1/source"];
        if (scenario == "missing") args = [];
        else if (scenario == "extra") args = [.. args, "private-extra-canary"];
        else if (scenario == "ip") args[0] = "private-proxy-canary";
        else if (scenario == "loopback") args[0] = "127.0.0.1";
        else if (scenario == "any") args[0] = "0.0.0.0";
        else if (scenario == "ipv6") args[0] = "fd00::1";
        else if (scenario == "host") args[1] = "*.privensic.com";
        else if (scenario == "owner") args[2] = "private-owner-canary";
        else if (scenario == "empty-owner") args[2] = new string('0', 32);
        else if (scenario == "uppercase-owner") args[2] = args[2].ToUpperInvariant();
        else args[3] = "https://credential-canary@screenreader.privensic.com/";
        using var input = new MemoryStream([1, 2, 3]); using var output = new MemoryStream(); using var errors = new StringWriter();
        var code = await PublicReaderWorker.RunAsync(args, input, output, errors);
        Assert.Equal(2, code); Assert.Equal(0, input.Position); Assert.Equal(0, output.Length);
        Assert.Equal("PUBLIC_READER_WORKER_CONFIGURATION_REQUIRED", errors.ToString().Trim());
    }
}
