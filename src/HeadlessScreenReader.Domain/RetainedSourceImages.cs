using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace HeadlessScreenReader.Domain;

/// <summary>One committed source image with its original delivery identity and raw Chromium presentation time; not decoded-media or speech proof.</summary>
public sealed record RetainedSourceImageFile(int SourceSequence, string File, double PresentationUnixMilliseconds,
    int Bytes, string Sha256, string ContentType = "image/jpeg")
{
    public const int MaximumBytes = 512 * 1024;
    public void Validate() => RetainedSourceImages.Require(SourceSequence is >= 1 and <= RetainedSourceImages.MaximumFrames
        && File == $"source-{SourceSequence:D4}.jpg" && double.IsFinite(PresentationUnixMilliseconds) && PresentationUnixMilliseconds > 0
        && Bytes is >= 4 and <= MaximumBytes && RetainedSourceImages.Digest(Sha256) && ContentType == "image/jpeg");
}

/// <summary>A checked committed image subset under the admitted producer. Missing pending files are never promoted to original images.</summary>
public sealed record RetainedSourceImages(string Schema, FixtureJob Job, Guid WorkerId, string PrefixSha256,
    ImmutableArray<RetainedSourceImageFile> Files, long Bytes, bool HasUncommittedFiles,
    string Scope = "RetainedOriginalSourceSubset", string Completeness = "NotAssessed",
    bool IsCompletedRecording = false, bool CanReplay = false, string GoalVerification = "NotAssessed")
{
    public const string CurrentSchema = "hsr-retained-source-images-v1";
    public const int MaximumFrames = 250;
    public const long MaximumBytes = 25 * 1024 * 1024;
    public void Validate()
    {
        ValidateIdentity(Job, WorkerId, PrefixSha256);
        Require(Schema == CurrentSchema && !Files.IsDefault && Files.Length <= MaximumFrames
            && Files.All(file => file is not null) && Files.Select(file => file.SourceSequence).Distinct().Count() == Files.Length);
        foreach (var file in Files) file.Validate();
        Require(Bytes == Files.Sum(file => (long)file.Bytes) && Bytes is >= 0 and <= MaximumBytes);
        ValidateScope(Scope, Completeness, IsCompletedRecording, CanReplay, GoalVerification);
    }
    internal static void ValidateIdentity(FixtureJob job, Guid worker, string prefix)
    {
        Require(job is not null); job.Validate();
        Require(job.RecordObservation && job.TraversalRoute is >= 1 and <= 4 && worker != Guid.Empty && Digest(prefix));
    }
    internal static void ValidateScope(string scope, string completeness, bool completed, bool replay, string goal) =>
        Require(scope == "RetainedOriginalSourceSubset" && completeness == "NotAssessed" && !completed && !replay && goal == "NotAssessed");
    internal static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
    internal static void Require([DoesNotReturnIf(false)] bool value)
    { if (!value) throw new BrowserOperationException("WORKER_SOURCE_IMAGES_INVALID"); }
}

/// <summary>A compact exact-image identity for paging; omits the other images and all private host/native wrappers.</summary>
public sealed record RetainedSourceImageReference(string Schema, FixtureJob Job, Guid WorkerId, string PrefixSha256,
    RetainedSourceImageFile Image, string Scope = "RetainedOriginalSourceSubset", string Completeness = "NotAssessed",
    bool IsCompletedRecording = false, bool CanReplay = false, string GoalVerification = "NotAssessed")
{
    public const string CurrentSchema = "hsr-retained-source-image-v1";
    public void Validate()
    {
        RetainedSourceImages.ValidateIdentity(Job, WorkerId, PrefixSha256);
        RetainedSourceImages.Require(Schema == CurrentSchema && Image is not null); Image.Validate();
        RetainedSourceImages.ValidateScope(Scope, Completeness, IsCompletedRecording, CanReplay, GoalVerification);
    }
}

/// <summary>At most64KiB of one verified original image, or an empty exact-EOF reauthorization page. Never a complete recording receipt.</summary>
public sealed record RetainedSourceImagePage(RetainedSourceImageReference Source, int Offset, byte[] Data,
    string PageSha256, int? NextOffset)
{
    public const int PageBytes = 65536;
}
