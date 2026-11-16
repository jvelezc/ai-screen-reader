using System.Buffers.Binary;
using System.Security.Cryptography;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class ReaderWireFailureFingerprintTests
{
    [Fact]
    public async Task InvalidFrameRetainsOnlyBoundedHeaderFactsAndNeverReadsTheUntrustedBody()
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, 1025);
        using var source = new MemoryStream([.. header, .. System.Text.Encoding.UTF8.GetBytes("private-provider-output-canary")]);
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => ReaderWorkerWire.ReadAsync(source, 1024));
        Assert.Equal("READER_WORKER_FRAME_INVALID", error.Code); Assert.Equal(error.Code, error.Message);
        Assert.Equal(4, source.Position); Assert.Equal(3, error.Data.Count);
        Assert.Equal(1025, error.Data["DeclaredBytes"]); Assert.Equal(1024, error.Data["MaximumBytes"]);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(header)), error.Data["HeaderSha256"]);
        Assert.DoesNotContain("private-provider-output-canary", error.ToString());
    }
}
