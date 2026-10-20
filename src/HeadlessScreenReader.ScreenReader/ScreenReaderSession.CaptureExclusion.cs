using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.ScreenReader;

public sealed partial class ScreenReaderSession
{
    public CaptureStatus CaptureStatus => _readerCapture.Status;

    /// <summary>Trusted local control only. Wait cancellation never acknowledges exclusion.</summary>
    public Task<CaptureExclusionLease> ExcludeCaptureAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_browser is not ICaptureControl adapter)
            throw new BrowserOperationException("READER_CAPTURE_CONTROL_UNAVAILABLE");
        return _readerCapture.ExcludeAsync(adapter, () =>
        {
            _snapshot = null;
            _cursorId = null;
            _mode = ReaderMode.Browse;
            _text.Clear();
            _liveText.Clear();
        });
    }

    /// <summary>The trusted owner must clear sensitive content before requesting a fresh baseline.</summary>
    public Task<AccessibilitySnapshot> ResumeCaptureAsync(CaptureExclusionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_browser is not ICaptureControl adapter)
            throw new BrowserOperationException("READER_CAPTURE_CONTROL_UNAVAILABLE");
        return _readerCapture.ResumeAsync(adapter, lease);
    }
}
