using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Reader-facing image subsets omit private native ownership and never grant replay or completion authority.</summary>
public static class SourceImagesProjection
{
    public static RetainedSourceImages Manifest(RecordedSourceSnapshot snapshot, FixtureJob expected, Guid producer)
    {
        Require(snapshot is not null && snapshot.Header is not null && snapshot.Header.Native is not null);
        var header = snapshot.Header;
        OwnershipJournal.Validate(header.Native); expected.Validate();
        Require(header.Schema == "hsr-worker-source-journal-v1" && header.Job == expected && header.WorkerId == producer
            && producer != Guid.Empty && header.Native.Job == (expected with { RecordObservation = false, TraversalRoute = 0 })
            && !snapshot.Frames.IsDefault && snapshot.Frames.All(frame => frame is not null && double.IsFinite(frame.PresentationUnixMilliseconds)));
        var prefix = TraversalArchive.Hash(FixtureProgressProtocol.Encode(new { snapshot.Header, snapshot.Frames, snapshot.Bytes, snapshot.HasUncommittedFiles }));
        var result = new RetainedSourceImages(RetainedSourceImages.CurrentSchema, expected, producer, prefix,
            [.. snapshot.Frames.Select(frame => new RetainedSourceImageFile(frame.SourceSequence, frame.File,
                frame.PresentationUnixMilliseconds, frame.Bytes, frame.Sha256))], snapshot.Bytes, snapshot.HasUncommittedFiles);
        result.Validate(); return result;
    }

    public static RetainedSourceImagePage Page(RetainedSourceImages manifest, int sequence, byte[] original, int offset)
    {
        manifest.Validate();
        var image = manifest.Files.SingleOrDefault(file => file.SourceSequence == sequence);
        Require(image is not null && original is not null && original.Length == image.Bytes
            && original.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }) && TraversalArchive.Hash(original) == image.Sha256
            && offset >= 0 && offset <= image.Bytes && (offset == image.Bytes || offset % RetainedSourceImagePage.PageBytes == 0));
        var source = new RetainedSourceImageReference(RetainedSourceImageReference.CurrentSchema, manifest.Job, manifest.WorkerId,
            manifest.PrefixSha256, image);
        source.Validate();
        var bytes = original.AsSpan(offset, Math.Min(RetainedSourceImagePage.PageBytes, original.Length - offset)).ToArray();
        var next = offset + bytes.Length;
        return new(source, offset, bytes, TraversalArchive.Hash(bytes), next < original.Length ? next : null);
    }
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value)
    { if (!value) throw new BrowserOperationException("WORKER_SOURCE_IMAGES_INVALID"); }
}
