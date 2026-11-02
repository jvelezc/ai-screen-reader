using System.Text;
using HeadlessScreenReader.Cli;

namespace HeadlessScreenReader.Tests;

public sealed class PublicReaderWorkerProofDiagnosticsTests
{
    private const string Known = "time=\"2026-11-02T09:25:59-08:00\" level=warning msg=\"Failed to close STDIN for writing: EOF\"\n";
    [Theory]
    [InlineData("empty-podman", "podman", 0)]
    [InlineData("empty-docker", "docker", 0)]
    [InlineData("known-podman", "podman", 1)]
    public void OnlyNamedRuntimeEofWarningIsRetained(string scenario, string runtime, int expected)
    {
        Assert.Equal(expected, PublicReaderWorkerProof.ClassifyDiagnostics(Encoding.UTF8.GetBytes(scenario == "known-podman" ? Known : ""), runtime));
    }

    [Theory]
    [InlineData("wrong-runtime")]
    [InlineData("different-message")]
    [InlineData("extra-message")]
    [InlineData("duplicate")]
    [InlineData("bad-encoding")]
    [InlineData("oversized")]
    public void UnknownOrUnboundedDiagnosticsCannotBecomeAcceptance(string scenario)
    {
        var bytes = scenario switch
        {
            "different-message" => Encoding.UTF8.GetBytes(Known.Replace("EOF", "native shutdown failed", StringComparison.Ordinal)),
            "extra-message" => Encoding.UTF8.GetBytes(Known + "PUBLIC_READER_WORKER_UNCONFIRMED\n"),
            "duplicate" => Encoding.UTF8.GetBytes(Known + Known),
            "bad-encoding" => [255],
            "oversized" => Encoding.UTF8.GetBytes(Known.Replace("2026-11-02", new string('2', 16384), StringComparison.Ordinal)),
            _ => Encoding.UTF8.GetBytes(Known)
        };
        Assert.Equal("PUBLIC_READER_DIAGNOSTICS_UNCONFIRMED", Assert.Throws<InvalidDataException>(() => PublicReaderWorkerProof.ClassifyDiagnostics(bytes, scenario == "wrong-runtime" ? "docker" : "podman")).Message);
    }
}
