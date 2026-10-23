using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>The read-only API sends only verified scope metadata to the trusted worker; it launches no native process.</summary>
internal sealed class PortalHostedReaderBrowser(ReaderHostClient host, Guid subject, Guid organization, Guid session) : IPortalReaderBrowser
{
    private ReaderHostEndpoint? _endpoint;
    private string? _document;
    private bool _attempted;
    public int InitialCaptures => _document is null ? 0 : 1;
    public bool CleanupConfirmed { get; private set; }
    public bool NativeShutdownConfirmed { get; private set; }

    public async Task InitializeAsync()
    {
        if (_attempted || CleanupConfirmed) throw new InvalidOperationException();
        _endpoint = host.Endpoint(); _attempted = true;
        var response = await host.ExchangeAsync(_endpoint, ReaderHostOperation.Open, subject, organization, session).ConfigureAwait(false);
        Check(response);
        _document = response.DocumentId!;
    }

    public async Task<AccessibilitySnapshot> CaptureAsync()
    {
        if (_endpoint is null || _document is null || CleanupConfirmed) throw new PortalFault(503, "CONTROL_READER_CAPTURE_UNCONFIRMED");
        // A worker restart cannot silently reopen a page or substitute a new document for this session.
        if (host.Endpoint().Instance != _endpoint.Instance) throw new PortalFault(503, "CONTROL_READER_RUNTIME_CHANGED");
        var response = await host.ExchangeAsync(_endpoint, ReaderHostOperation.Observe, subject, organization, session, _document).ConfigureAwait(false);
        Check(response); return response.Snapshot!;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_attempted) { CleanupConfirmed = true; return; }
        // Reconnect for retirement after a lost response. A restarted worker must first recover its durable inventory.
        var response = await host.ExchangeAsync(host.Endpoint(), ReaderHostOperation.Retire, subject, organization, session).ConfigureAwait(false);
        Check(response);
        CleanupConfirmed = response.Retirement!.ContainerAbsenceConfirmed;
        NativeShutdownConfirmed = response.Retirement.NativeShutdownConfirmed;
        if (!CleanupConfirmed || _document is not null && !NativeShutdownConfirmed)
            throw new PortalFault(503, "CONTROL_READER_CLOSE_UNCONFIRMED");
    }

    private static void Check(ReaderHostResponse response)
    {
        if (response.ErrorCode is null) return;
        throw response.ErrorCode switch
        {
            "READER_HOST_SUBJECT_ALREADY_OPEN" => new PortalFault(409, "CONTROL_READER_ALREADY_OPEN"),
            "READER_HOST_CAPACITY_EXHAUSTED" => new PortalFault(409, "CONTROL_READER_CAPACITY_EXHAUSTED"),
            _ => new PortalFault(503, "CONTROL_READER_OPERATION_UNCONFIRMED")
        };
    }
}
