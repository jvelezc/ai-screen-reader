using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record FixtureSourceFrame(int SourceSequence, string File, double PresentationUnixMilliseconds,
    string Sha256, byte[] Content);
public sealed record FixtureSourceBatch(string Schema, Guid ExecutionId, int Sequence, ImmutableArray<FixtureSourceFrame> Frames);
public sealed record FixtureSourceAck(string Schema, Guid ExecutionId, int Sequence, string BatchSha256);

/// <summary>Original JPEG subsets are separate from command progress and complete recording bundles.</summary>
public static class FixtureSourceProtocol
{
    public const string Schema = "hsr-fixture-source-frames-v1";
    public const int MaximumBatchBytes = 512 * 1024;
    public const int MaximumFrames = 250;
    public const int MaximumTotalBytes = 25 * 1024 * 1024;

    public static void Validate(FixtureSourceBatch value, FixtureProgressStart start, int expectedSequence)
    {
        FixtureProgressProtocol.Validate(start);
        Require(value is not null && start.RetainSourceFrames && value.Schema == Schema && value.ExecutionId == start.ExecutionId
            && value.Sequence == expectedSequence && value.Sequence is >= 1 and <= MaximumFrames
            && !value.Frames.IsDefaultOrEmpty && value.Frames.Length <= 64);
        long bytes = 0;
        var sources = new HashSet<int>();
        foreach (var frame in value.Frames)
        {
            Require(frame is not null && frame.SourceSequence is >= 1 and <= MaximumFrames
                && sources.Add(frame.SourceSequence) && frame.File == $"source-{frame.SourceSequence:D4}.jpg"
                && double.IsFinite(frame.PresentationUnixMilliseconds) && frame.PresentationUnixMilliseconds > 0
                && frame.Content is { Length: >= 4 and <= MaximumBatchBytes }
                && frame.Content.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff })
                && frame.Sha256 is { Length: 64 } && frame.Sha256.All(char.IsAsciiHexDigitLower)
                && TraversalArchive.Hash(frame.Content) == frame.Sha256);
            bytes += frame.Content.Length;
            Require(bytes <= MaximumBatchBytes);
        }
    }

    public static FixtureSourceBatch Snapshot(FixtureSourceBatch value, FixtureProgressStart start, int expectedSequence)
    {
        Validate(value, start, expectedSequence);
        var snapshot = value with { Frames = [.. value.Frames.Select(f => f with { Content = f.Content.ToArray() })] };
        Validate(snapshot, start, expectedSequence);
        return snapshot;
    }

    public static void Validate(FixtureSourceAck ack, Guid execution, int sequence, string digest) =>
        Require(ack is not null && ack.Schema == Schema && ack.ExecutionId == execution && ack.Sequence == sequence && ack.BatchSha256 == digest);

    public static bool IsSourceBatch(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes, new() { AllowDuplicateProperties = false });
        return json.RootElement.TryGetProperty("Schema", out var schema) && schema.ValueKind == JsonValueKind.String
            && schema.GetString() == Schema;
    }

    internal static void Require([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition) throw new BrowserOperationException("WORKER_PROGRESS_MEDIA_INVALID");
    }
}
