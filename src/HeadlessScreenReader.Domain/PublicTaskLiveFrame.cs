using System.Buffers.Binary;

namespace HeadlessScreenReader.Domain;

/// <summary>A disposable browser image from one active public task attempt.</summary>
public sealed record PublicTaskLiveFrame(string Schema, Guid OrganizationId, Guid ScanId, Guid JobId, Guid ExecutionId,
    int Attempt, int Sequence, DateTimeOffset CapturedAt, int Width, int Height, byte[] Data, string Sha256)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PublicTaskLivePointer? Pointer { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? LastKey { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? FocusName { get; init; }

    public const string CurrentSchema = "hsr-public-task-live-frame-v1";
    public const int MaximumImageBytes = 160 * 1024;
    public const int MaximumEnvelopeBytes = 224 * 1024;
    public static TimeSpan MaximumAge => TimeSpan.FromSeconds(8);

    public bool IsCurrent(PublicTaskStart start, int attempt, DateTimeOffset now) =>
        Schema == CurrentSchema && OrganizationId == start.Job.OrganizationId && ScanId == start.Job.ParentScanId
        && JobId == start.Job.JobId && ExecutionId == start.ExecutionId && Attempt is >= 1 and <= 3 && Attempt == attempt
        && Sequence > 0 && CapturedAt.Offset == TimeSpan.Zero && CapturedAt <= now.AddSeconds(1) && now - CapturedAt <= MaximumAge
        && (Pointer is null || Attempt == 3 && Pointer.X is >= 0 and < 10000 && Pointer.Y is >= 0 and < 10000)
        && (LastKey is null || Attempt < 3 && LastKey is "Tab" or "Enter")
        && (FocusName is null || Attempt < 3 && PublicTaskRules.Text(FocusName, 160))
        && TryJpegSize(Data, out var width, out var height) && width == Width && height == Height && PublicTaskRules.Hash(Data) == Sha256;

    public static bool TryJpegSize(byte[]? data, out int width, out int height)
    {
        width = 0; height = 0;
        if (data is null || data.Length is < 13 or > MaximumImageBytes || data[0] != 0xff || data[1] != 0xd8
            || data[^2] != 0xff || data[^1] != 0xd9) return false;
        for (var marker = 2; marker + 3 < data.Length;)
        {
            if (data[marker++] != 0xff) return false;
            while (marker < data.Length && data[marker] == 0xff) marker++;
            if (marker + 2 >= data.Length) return false;
            var type = data[marker++];
            var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(marker));
            if (length < 2 || marker + length > data.Length || type is 0xda or 0xd9) return false;
            if (type is 0xc0 or 0xc2)
            {
                if (length < 8 || data[marker + 2] != 8) return false;
                height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(marker + 3));
                width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(marker + 5));
                return width is >= 1 and <= 1280 && height is >= 1 and <= 720;
            }
            marker += length;
        }
        return false;
    }
}

public sealed record PublicTaskLivePointer(int X, int Y, bool Pressed);
