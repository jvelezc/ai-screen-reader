using System.Buffers.Binary;
using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using Microsoft.Extensions.Time.Testing;
using static HeadlessScreenReader.Tests.OwnedScanSessionTests;

namespace HeadlessScreenReader.Tests;

public sealed class PrivatePixelTests
{
    internal static ImmutableArray<byte> Envelope()
    {
        var bytes = new byte[57]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13); "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), 320); BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), 240);
        bytes[24] = 8; bytes[25] = 2; "IEND"u8.CopyTo(bytes.AsSpan(49)); return ImmutableArray.CreateRange(bytes);
    }
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static (ViewerScope Scope, Guid Subject, PrivateViewerBroker Broker, ViewerConnection Connection) Setup(TimeProvider? time = null)
    {
        var scope = new ViewerScope(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")); var subject = Guid.NewGuid();
        var broker = new PrivateViewerBroker(scope, subject, time);
        var connection = broker.Exchange(subject, scope, broker.IssueBootstrap(subject, scope, TimeSpan.FromMinutes(1)).Secret.Value, TimeSpan.FromMinutes(1));
        return (scope, subject, broker, connection);
    }
    [Theory]
    [InlineData("signature")]
    [InlineData("ihdr")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("depth")]
    [InlineData("color")]
    [InlineData("compression")]
    [InlineData("filter")]
    [InlineData("interlace")]
    [InlineData("iend")]
    [InlineData("header-length")]
    [InlineData("short")]
    [InlineData("oversize")]
    [InlineData("default")]
    public void PixelEnvelope_RejectsUnsupportedDimensionsFormatsAndBounds(string partition)
    {
        var bytes = Envelope().ToArray();
        switch (partition)
        {
            case "signature": bytes[0] = 0; break;
            case "ihdr": bytes[12] = 0; break;
            case "header-length": bytes[11] = 12; break;
            case "width": bytes[19]++; break;
            case "height": bytes[23]++; break;
            case "depth": bytes[24] = 16; break;
            case "color": bytes[25] = 3; break;
            case "compression": bytes[26] = 1; break;
            case "filter": bytes[27] = 1; break;
            case "interlace": bytes[28] = 1; break;
            case "iend": bytes[49] = 0; break;
            case "short": bytes = new byte[56]; break;
            case "oversize": bytes = new byte[ViewerPixelFrame.MaximumBytes + 1]; break;
        }
        Assert.True(ViewerPixelFrame.HasBoundedPngEnvelope(Envelope()));
        Assert.False(ViewerPixelFrame.HasBoundedPngEnvelope(partition == "default" ? default : ImmutableArray.CreateRange(bytes)));
    }
    [Fact]
    public void PixelEnvelope_AcceptsRgbAndRgbaAtTheExactByteCeilingWithoutClaimingDecode()
    {
        var rgb = Envelope().ToArray(); rgb[25] = 6;
        Assert.True(ViewerPixelFrame.HasBoundedPngEnvelope(ImmutableArray.CreateRange(rgb)));
        var ceiling = new byte[ViewerPixelFrame.MaximumBytes]; rgb.CopyTo(ceiling, 0); rgb.AsSpan(^12).CopyTo(ceiling.AsSpan(ceiling.Length - 12));
        Assert.True(ViewerPixelFrame.HasBoundedPngEnvelope(ImmutableArray.CreateRange(ceiling)));
    }
    [Theory]
    [InlineData("unowned")]
    [InlineData("no-baseline")]
    [InlineData("excluded")]
    [InlineData("stale")]
    [InlineData("closed")]
    public async Task OwnerPixels_DenyUnavailableOwnershipOrBaselineBeforeNativeCapture(string partition)
    {
        var browser = new PixelBrowser(); await using var control = new ControlledScanSession(browser, new RecordingStore());
        if (partition != "unowned")
        {
            var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            if (partition is "excluded" or "stale")
            {
                await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent); await control.ReleaseAsync(ai);
                if (partition == "stale") await control.AcquireAsync(TimeSpan.FromMinutes(1));
            }
            if (partition == "closed") await control.DisposeAsync();
        }
        var sent = false;
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => control.DeliverPixelsAsync(1, _ => { sent = true; return Task.CompletedTask; }));
        Assert.Equal(partition switch { "no-baseline" => "OWNED_VIEW_UNAVAILABLE", "stale" => "OWNED_SCAN_EPOCH_STALE", "closed" => "CONTROL_CLOSED", _ => "CONTROL_VIEW_UNAVAILABLE" }, error.Code);
        Assert.False(browser.Started.Task.IsCompleted); Assert.False(sent); Assert.Equal(0, browser.Inner.Keys);
    }
    [Fact]
    public async Task OwnerPixels_BindAnAcknowledgedBaselineAndNeverWritePixelPayloadOrDispatchInput()
    {
        var browser = new PixelBrowser(); var store = new RecordingStore(); await using var control = new ControlledScanSession(browser, store);
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1)); await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent);
        var records = store.Records.Length; browser.Finish.SetResult(new("owned-document", 1, Envelope())); ViewerPixelFrame? sent = null;
        await control.DeliverPixelsAsync(1, value => { sent = value; return Task.CompletedTask; });
        Assert.Equal((store.ScanId, 1L, 1L, "owned-document", false), (sent!.ScanId, sent.Epoch, sent.BaselineOrdinal, sent.DocumentId, sent.PriorSimulatedAssistance));
        Assert.Equal(Envelope().ToArray(), sent.Png.ToArray()); Assert.Equal(records, store.Records.Length);
        Assert.Equal(0, browser.Inner.Keys); Assert.Equal(0, control.Status.Owned.ActiveOperations);
    }
    [Theory]
    [InlineData("scan")]
    [InlineData("epoch")]
    [InlineData("ordinal")]
    [InlineData("document")]
    [InlineData("png")]
    [InlineData("null")]
    [InlineData("long-document")]
    public async Task PixelBroker_RejectsMisboundPayloadBeforeAnyDelivery(string partition)
    {
        var s = Setup(); await using var broker = s.Broker; var deliveries = 0;
        var frame = new ViewerPixelFrame(s.Scope.ScanId, 1, 0, "document", Envelope(), false);
        frame = partition switch
        {
            "scan" => frame with { ScanId = "foreign" },
            "epoch" => frame with { Epoch = 2 },
            "ordinal" => frame with { BaselineOrdinal = -1 },
            "document" => frame with { DocumentId = "" },
            "null" => null!,
            "long-document" => frame with { DocumentId = new string('d', 1025) },
            _ => frame with { Png = [] }
        };
        Assert.Equal("VIEWER_SOURCE_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => broker.DeliverPixelsAsync(
            s.Subject, s.Scope, s.Connection.Id, s.Connection.Secret.Value, 1, (send, _) => send(frame), (_, _) => { deliveries++; return Task.CompletedTask; }))).Code);
        Assert.Equal(0, deliveries); Assert.Equal(0, broker.Status.ActiveReads);
    }
    [Theory]
    [InlineData("subject")]
    [InlineData("scan")]
    [InlineData("session")]
    [InlineData("connection")]
    [InlineData("secret")]
    [InlineData("expired")]
    [InlineData("revoked")]
    public async Task PixelBroker_RequiresTheSameScopedMonotonicAuthorityBeforeSource(string partition)
    {
        var time = new FakeTimeProvider(); var s = Setup(time); await using var broker = s.Broker; var calls = 0;
        if (partition == "expired") time.Advance(TimeSpan.FromMinutes(1));
        if (partition == "revoked") await broker.RevokeAsync(s.Subject, s.Scope, s.Connection.Id, s.Connection.Secret.Value);
        var scope = partition == "scan" ? s.Scope with { ScanId = Guid.NewGuid().ToString("N") }
            : partition == "session" ? s.Scope with { SessionId = Guid.NewGuid().ToString("N") } : s.Scope;
        await Assert.ThrowsAsync<BrowserOperationException>(() => broker.DeliverPixelsAsync(partition == "subject" ? Guid.NewGuid() : s.Subject,
            scope, partition == "connection" ? Guid.NewGuid() : s.Connection.Id, partition == "secret" ? new string('0', 64) : s.Connection.Secret.Value, 1,
            (_, _) => { calls++; return Task.CompletedTask; }, (_, _) => Task.CompletedTask));
        Assert.Equal(0, calls); Assert.Equal(0, broker.Status.ActiveReads);
    }
    [Fact]
    public async Task PixelAndSemanticReads_ShareOneConnectionBusyFenceAndActualRevokeDrain()
    {
        var s = Setup(); await using var broker = s.Broker; var sendStarted = Signal(); var finish = Signal();
        var read = broker.DeliverPixelsAsync(s.Subject, s.Scope, s.Connection.Id, s.Connection.Secret.Value, 1,
            async (send, _) => await send(new(s.Scope.ScanId, 1, 0, "document", Envelope(), false)),
            (_, _) => { sendStarted.SetResult(); return finish.Task; });
        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("VIEWER_READ_BUSY", (await Assert.ThrowsAsync<BrowserOperationException>(() => broker.DeliverAsync(s.Subject,
            s.Scope, s.Connection.Id, s.Connection.Secret.Value, 1, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask))).Code);
        using var caller = new CancellationTokenSource(); var revoke = broker.RevokeAsync(s.Subject, s.Scope, s.Connection.Id, s.Connection.Secret.Value, caller.Token);
        caller.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => revoke);
        Assert.Equal(1, broker.Status.ActiveReads); finish.SetResult(); await read;
        await broker.RevokeAsync(s.Subject, s.Scope, s.Connection.Id, s.Connection.Secret.Value);
        Assert.Equal(0, broker.Status.ActiveReads); Assert.Equal(0, broker.Status.ActiveConnections);
    }
    [Fact]
    public async Task PixelRevocation_DropsLateSourceBytesAndJoinsNonCooperativeActualCapture()
    {
        var s = Setup(); await using var broker = s.Broker; var started = Signal(); var finish = Signal(); var deliveries = 0;
        var read = broker.DeliverPixelsAsync(s.Subject, s.Scope, s.Connection.Id, s.Connection.Secret.Value, 1,
            async (send, _) => { started.SetResult(); await finish.Task; await send(new(s.Scope.ScanId, 1, 0, "document", Envelope(), false)); },
            (_, _) => { deliveries++; return Task.CompletedTask; });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); var revoke = broker.RevokeAsync(s.Subject, s.Scope, s.Connection.Id, s.Connection.Secret.Value);
        Assert.False(revoke.IsCompleted); Assert.Equal(1, broker.Status.ActiveReads); finish.SetResult();
        await Assert.ThrowsAsync<BrowserOperationException>(() => read); await revoke;
        Assert.Equal(0, deliveries); Assert.Equal(0, broker.Status.ActiveReads);
    }
    [Fact]
    public async Task OwnerPixelCapture_JoinsActualWorkAfterCancellationAndBlocksPublicationAcrossExclusion()
    {
        var browser = new PixelBrowser(); var store = new RecordingStore(); await using var control = new ControlledScanSession(browser, store);
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1)); await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent);
        var records = store.Records.Length; var deliveries = 0;
        using var cancel = new CancellationTokenSource();
        var read = control.DeliverPixelsAsync(1, _ => { deliveries++; return Task.CompletedTask; }, cancel.Token); await browser.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel(); Assert.False(read.IsCompleted); Assert.Equal(1, control.Status.Owned.ActiveOperations);
        var release = control.ReleaseAsync(ai); Assert.False(release.IsCompleted); browser.Finish.SetResult(new("owned-document", 1, Envelope()));
        Assert.Equal("OWNED_SCAN_EXCLUDED", (await Assert.ThrowsAsync<BrowserOperationException>(() => read)).Code); await release;
        Assert.Equal(0, deliveries); Assert.Equal(0, control.Status.Owned.ActiveOperations); Assert.Equal(0, browser.Inner.Keys);
        Assert.Equal(records + 2, store.Records.Length); // lifecycle drain/excluded only; pixels never enter a result record
        Assert.DoesNotContain(store.Records, r => r is ViewerPixelFrame or BrowserPixelFrame);
    }
    [Theory]
    [InlineData("document")]
    [InlineData("epoch")]
    [InlineData("png")]
    [InlineData("null")]
    public async Task OwnerPixelCapture_RejectsAdapterBindingMismatchWithoutPublishing(string partition)
    {
        var browser = new PixelBrowser(); await using var control = new ControlledScanSession(browser, new RecordingStore());
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1)); await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent);
        var calls = 0; var read = control.DeliverPixelsAsync(1, _ => { calls++; return Task.CompletedTask; }); await browser.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        browser.Finish.SetResult(partition == "null" ? null! : new(partition == "document" ? "other" : "owned-document", partition == "epoch" ? 2 : 1, partition == "png" ? [] : Envelope()));
        Assert.Equal("PIXEL_SOURCE_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => read)).Code);
        Assert.Equal(0, calls); Assert.Equal(0, control.Status.Owned.ActiveOperations); Assert.Equal(0, browser.Inner.Keys);
    }
    [Fact]
    public async Task ControllerPixelForwarder_DeniesAnAdapterWithoutTheOptionalCapability()
    {
        var browser = new OwnedBrowser(); await using var control = new ControlledScanSession(browser, new RecordingStore());
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1)); await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent);
        var delivered = false;
        Assert.Equal("OWNED_VIEW_UNAVAILABLE", (await Assert.ThrowsAsync<BrowserOperationException>(() => control.DeliverPixelsAsync(1,
            _ => { delivered = true; return Task.CompletedTask; }))).Code);
        Assert.False(delivered); Assert.Equal(0, browser.Keys); Assert.Equal(0, control.Status.Owned.ActiveOperations);
    }
    internal sealed class PixelBrowser : IScreenReaderBrowser, ICaptureControl, IPrivatePixelCapture, IAsyncDisposable
    {
        public OwnedBrowser Inner { get; } = new();
        public TaskCompletionSource Started { get; } = Signal();
        public TaskCompletionSource<BrowserPixelFrame> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CaptureStatus CaptureStatus => Inner.CaptureStatus;
        public async Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => (await Inner.CaptureAsync(cancellationToken)) with { DocumentUrl = null };
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) => Inner.PressKeyAsync(key, cancellationToken);
        public Task<CaptureExclusionLease> ExcludeCaptureAsync() => Inner.ExcludeCaptureAsync();
        public async Task<AccessibilitySnapshot> ResumeCaptureAsync(CaptureExclusionLease lease) => (await Inner.ResumeCaptureAsync(lease)) with { DocumentUrl = null };
        public Task<BrowserPixelFrame> CapturePixelsAsync(long epoch, string documentId, CancellationToken cancellationToken = default) { Started.TrySetResult(); return Finish.Task; }
        public ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
}
