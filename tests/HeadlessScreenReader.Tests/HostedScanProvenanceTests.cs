using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;

namespace HeadlessScreenReader.Tests;

public sealed partial class HostedScanTests
{
    [Theory]
    [InlineData("tenant")]
    [InlineData("scan")]
    [InlineData("origin")]
    [InlineData("worker")]
    [InlineData("archive")]
    [InlineData("readback")]
    [InlineData("generation")]
    [InlineData("state")]
    public async Task McpRejectsUnconfirmedProducerProvenanceWithoutReturningObservation(string failure)
    {
        var root = Tree(Report());
        root["workerArchive"] = Tree(WorkerEvidenceTests.Receipt(new(Organization, ScanId, JobId, 1)));
        switch (failure)
        {
            case "tenant": root["workerArchive"]!["job"]!["organizationId"] = Guid.NewGuid(); break;
            case "scan": root["workerArchive"]!["job"]!["scanId"] = Guid.NewGuid(); break;
            case "origin": root["workerArchive"]!["origin"] = "OperatorImport"; break;
            case "worker": root["workerArchive"]!["workerId"] = Guid.Empty; break;
            case "archive": root["workerArchive"]!["receipt"]!["identity"]!["archiveId"] = Guid.NewGuid(); break;
            case "readback": root["workerArchive"]!["receipt"]!["objectReadbackVerified"] = false; break;
            case "generation": root["workerArchive"]!["job"]!["generation"] = 4; break;
            default: root["scan"]!["state"] = "leased"; break;
        }
        await using var client = new HostedScanClient(Options(), Queue(() => Json(root)));
        var reply = await client.ReportAsync(ScanId.ToString());
        Assert.Equal("HOSTED_RESPONSE_INVALID", reply.ErrorCode);
        Assert.DoesNotContain("Synthetic shop", reply.ToJson());
    }

    [Fact]
    public async Task McpRetainsProducerReceiptForExplicitArchiveRetrievalAfterReconnect()
    {
        var receipt = WorkerEvidenceTests.Receipt(new(Organization, ScanId, JobId, 1));
        var root = Tree(Report()); root["workerArchive"] = Tree(receipt);
        await using var first = new HostedScanClient(Options(), Queue(() => Json(root)));
        var page = Data(await first.ReportAsync(ScanId.ToString()));
        await first.DisposeAsync();
        await using var next = new HostedScanClient(Options(), Queue(() => Json(root)));
        var again = Data(await next.ReportAsync(ScanId.ToString(), reportId: page.GetProperty("reportId").GetString()));
        Assert.Equal(page.GetProperty("workerArchive").GetRawText(), again.GetProperty("workerArchive").GetRawText());
        Assert.Equal(receipt.Receipt.Identity.IndexSha256, again.GetProperty("workerArchive").GetProperty("receipt").GetProperty("identity").GetProperty("indexSha256").GetString());
        Assert.Equal(0, again.GetProperty("observation").GetProperty("dispatchedInputs").GetInt32());
        Assert.False(again.GetProperty("observation").GetProperty("goalVerified").GetBoolean());
    }
}
