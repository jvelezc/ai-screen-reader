using System.Buffers.Binary;
using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>Fresh fixed-fixture pixels. This is not a retained semantic observation or goal verification.</summary>
public sealed record BrowserPixelFrame(string DocumentId, long Epoch, ImmutableArray<byte> Png);
/// <summary>Optional capture capability, disabled unless the host opts into the fixed pixel fixture.</summary>
public interface IPrivatePixelCapture
{
    Task<BrowserPixelFrame> CapturePixelsAsync(long epoch, string documentId, CancellationToken cancellationToken = default);
}
/// <summary>Current fixture pixels bound to an acknowledged baseline. Never persist the encoded payload.</summary>
public sealed record ViewerPixelFrame(string ScanId, long Epoch, long BaselineOrdinal, string DocumentId,
    ImmutableArray<byte> Png, bool PriorSimulatedAssistance)
{
    public string PixelKind => "FreshFixturePixels";
    public const int Width = 320, Height = 240, MaximumBytes = 4 * 1024 * 1024;

    // Structural envelope validation only. Successful PNG decoding and visual identity are separate acceptance evidence.
    public static bool HasBoundedPngEnvelope(ImmutableArray<byte> png)
    {
        if (png.IsDefault || png.Length is < 57 or > MaximumBytes) return false;
        var bytes = png.AsSpan();
        return bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && BinaryPrimitives.ReadUInt32BigEndian(bytes[8..12]) == 13
            && bytes[12..16].SequenceEqual("IHDR"u8)
            && BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]) == Width
            && BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]) == Height
            && bytes[24] == 8 && bytes[25] is 2 or 6 && bytes[26] == 0 && bytes[27] == 0 && bytes[28] == 0
            && bytes[^12..^4].SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68 });
    }
}
