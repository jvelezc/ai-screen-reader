using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public enum ReaderHostOperation { Ready, Open, Observe, Retire }

/// <summary>Private, process-lifetime loopback authority. Never return this descriptor through HTTP or diagnostics.</summary>
public sealed record ReaderHostEndpoint(string Schema, Guid Instance, int Port, string Image, string Capability)
{
    public const string CurrentSchema = "hsr-private-reader-host-v1";
    public static string DescriptorPath(string root) => Path.Combine(Path.GetFullPath(root), ".reader-runtime", "host.json");
    public void Validate(string? expectedImage = null)
    {
        if (Schema != CurrentSchema || Instance == Guid.Empty || Port is < 1 or > 65535
            || !Regex.IsMatch(Image, "\\Asha256:[a-f0-9]{64}\\z") || expectedImage is not null && Image != expectedImage
            || !ValidCapability(Capability)) throw new BrowserOperationException("READER_HOST_IDENTITY_UNCONFIRMED");
    }
    internal static bool ValidCapability(string value)
    {
        Span<byte> bytes = stackalloc byte[32];
        return value is not null && value.Length == 44 && Convert.TryFromBase64String(value, bytes, out var count) && count == 32;
    }
    public static ReaderHostEndpoint Read(string root, string? expectedImage = null)
    {
        var path = DescriptorPath(root);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 1 or > 4096 || file.LinkTarget is not null)
            throw new BrowserOperationException("READER_HOST_UNAVAILABLE");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & ~(UnixFileMode.UserRead | UnixFileMode.UserWrite)) != 0)
            throw new BrowserOperationException("READER_HOST_DESCRIPTOR_UNPROTECTED");
        var endpoint = JsonSerializer.Deserialize<ReaderHostEndpoint>(File.ReadAllBytes(path), ReaderWorkerWire.Json)
            ?? throw new BrowserOperationException("READER_HOST_UNAVAILABLE");
        endpoint.Validate(expectedImage); return endpoint;
    }
    public override string ToString() => "ReaderHostEndpoint(private loopback authority redacted)";
}

/// <summary>One bounded RPC. User IDs are nonsecret scope metadata, never browser credentials.</summary>
public sealed record ReaderHostRequest(string Schema, Guid Instance, Guid RequestId, string Capability,
    ReaderHostOperation Operation, Guid Subject, Guid Organization, Guid Session)
{
    public void Validate(ReaderHostEndpoint endpoint)
    {
        if (Schema != ReaderHostEndpoint.CurrentSchema || Instance != endpoint.Instance || RequestId == Guid.Empty
            || !Enum.IsDefined(Operation) || !ReaderHostEndpoint.ValidCapability(Capability)
            || !CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(Capability), Convert.FromBase64String(endpoint.Capability))
            || Operation != ReaderHostOperation.Ready && (Subject == Guid.Empty || Organization == Guid.Empty || Session == Guid.Empty)
            || Operation == ReaderHostOperation.Ready && (Subject != Guid.Empty || Organization != Guid.Empty || Session != Guid.Empty))
            throw new BrowserOperationException("READER_HOST_REQUEST_DENIED");
    }
    public override string ToString() => "ReaderHostRequest(private authority redacted)";
}

/// <summary>Observed semantics and retirement facts; errors never masquerade as a successful observation or close.</summary>
public sealed record ReaderHostResponse(string Schema, Guid Instance, Guid RequestId, ReaderHostOperation Operation,
    Guid Subject, Guid Organization, Guid Session, string? ErrorCode, string? DocumentId,
    AccessibilitySnapshot? Snapshot, ReaderRetirement? Retirement, int DispatchedInputs, bool GoalVerified)
{
    public void Validate(ReaderHostRequest request, string? expectedDocument = null)
    {
        if (Schema != ReaderHostEndpoint.CurrentSchema || Instance != request.Instance || RequestId != request.RequestId
            || Operation != request.Operation || Subject != request.Subject || Organization != request.Organization || Session != request.Session
            || DispatchedInputs != 0 || GoalVerified)
            throw new BrowserOperationException("READER_HOST_RESPONSE_UNCONFIRMED");
        if (ErrorCode is not null)
        {
            if (!Regex.IsMatch(ErrorCode, "\\AREADER_[A-Z_]{1,80}\\z") || DocumentId is not null || Snapshot is not null || Retirement is not null)
                throw new BrowserOperationException("READER_HOST_RESPONSE_UNCONFIRMED");
            return;
        }
        if (Operation is ReaderHostOperation.Open or ReaderHostOperation.Observe)
        {
            if (string.IsNullOrWhiteSpace(DocumentId) || expectedDocument is not null && DocumentId != expectedDocument || Retirement is not null)
                throw new BrowserOperationException("READER_HOST_RESPONSE_UNCONFIRMED");
            if (Operation == ReaderHostOperation.Open && Snapshot is not null || Operation == ReaderHostOperation.Observe
                && (Snapshot is null || Snapshot.DocumentId != DocumentId || Snapshot.CaptureEpoch != 1
                    || Snapshot.Nodes.IsDefaultOrEmpty || Snapshot.Nodes.Length > ReaderWorkerRequest.MaximumNodes))
                throw new BrowserOperationException("READER_HOST_RESPONSE_UNCONFIRMED");
        }
        else if (DocumentId is not null || Snapshot is not null
            || Operation == ReaderHostOperation.Ready && Retirement is not null
            || Operation == ReaderHostOperation.Retire && Retirement is not { ContainerAbsenceConfirmed: true })
            throw new BrowserOperationException("READER_HOST_RESPONSE_UNCONFIRMED");
    }
}
