using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;

namespace HeadlessScreenReader.Tests;

public sealed partial class HostedScanTests
{
    [Fact]
    public async Task McpExplicitPassiveAdmissionForwardsModeWithoutInventingRecordedExecution()
    {
        var handler = Queue(() => Json(Admission(), 201)); await using var client = new HostedScanClient(Options(), handler);
        var request = Guid.NewGuid(); var reply = await client.StartFixtureAsync(request.ToString(), recordObservation: true);
        Assert.Null(reply.ErrorCode); var sent = Assert.Single(handler.Requests);
        using var body = JsonDocument.Parse(sent.Body!); Assert.True(body.RootElement.GetProperty("recordObservation").GetBoolean());
        Assert.Equal(request, body.RootElement.GetProperty("requestId").GetGuid());
        var result = Data(reply); Assert.Equal("PassiveObservation", result.GetProperty("recordingRequested").GetString());
        Assert.Equal("NotEstablishedByAdmission", result.GetProperty("execution").GetString()); Assert.Equal("NotEstablishedByAdmission", result.GetProperty("evidence").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task McpRequiresRecordingModeAndFourFileReceiptToAgree(bool correct)
    {
        var job = new FixtureJob(Organization, ScanId, JobId, 1) { RecordObservation = true };
        var producer = WorkerEvidenceTests.Receipt(job) with { Recording = "PassiveObservation" };
        producer = producer with { Receipt = producer.Receipt with { Files = correct ? 4 : 2 } };
        var report = Tree(Report()); report["workerArchive"] = Tree(producer);
        await using var client = new HostedScanClient(Options(), Queue(() => Json(report)));
        var result = await client.ReportAsync(ScanId.ToString());
        if (correct)
        {
            Assert.Null(result.ErrorCode); Assert.Equal("PassiveObservation", Data(result).GetProperty("workerArchive").GetProperty("recording").GetString());
            Assert.Equal(0, Data(result).GetProperty("observation").GetProperty("dispatchedInputs").GetInt32());
        }
        else Assert.Equal("HOSTED_RESPONSE_INVALID", result.ErrorCode);
    }
}
