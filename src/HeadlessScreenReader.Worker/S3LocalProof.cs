using System.Net;
using System.Security.Cryptography;
using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.Signing;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Actual S3 protocol proof using only disposable credentials and an isolated local service.</summary>
public static class S3LocalProof
{
    public static async Task<string[]> RunAsync(int port, Guid owner, string localRoot, FixtureJob job, byte[] bytes, CancellationToken token,
        string host = "127.0.0.1")
    {
        var cases = new List<string>();
        void Check(bool condition, string name)
        { if (!condition) throw new InvalidDataException("S3_PROOF_" + name); cases.Add(name); }
        var observation = ObservationStore.Validate(bytes, job);
        Check(observation.DispatchedInputs == 0 && !observation.GoalVerified, "NativeFixtureAndCleanupConfirmed");
        var configuration = S3EvidenceConfiguration.LocalProof(port, owner, host);
        using var client = configuration.CreateClient();
        using var archive = new S3ObservationArchive(configuration);
        var bucket = new Uri(configuration.Endpoint, configuration.Bucket);
        using (var create = await client.PutAsync(bucket, new ByteArrayContent([]), token).ConfigureAwait(false))
            Check(create.StatusCode == HttpStatusCode.OK, "PrivateBucketCreatedBySignedRequest");
        var stored = await new ObservationStore(localRoot, archive).PersistAsync(bytes, job, token).ConfigureAwait(false);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Check(stored.Sha256 == digest && stored.OffHostReadbackVerified, "SignedS3ReadbackMatchesExactNativeObservation");
        var local = Path.Combine(Path.GetFullPath(localRoot), job.Reference.Replace('/', Path.DirectorySeparatorChar));
        File.Delete(local);
        using var fresh = new S3ObservationArchive(configuration);
        var remote = await fresh.ReadAsync(job, digest, token).ConfigureAwait(false);
        Check(!File.Exists(local) && remote is not null && bytes.AsSpan().SequenceEqual(remote), "RemovingLocalFilePreservesOffHostReadback");
        await fresh.FinalizeAsync(job, bytes, digest, token).ConfigureAwait(false);
        Check(true, "ExistingVerifiedObjectCanBeReused");
        var address = new Uri(configuration.Endpoint, configuration.Bucket + "/" + S3ObservationArchive.ObjectKey(job, digest));
        using (var anonymous = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(10) })
        using (var denied = await anonymous.GetAsync(address, token).ConfigureAwait(false))
            Check(denied.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, "AnonymousCannotReadPrivateArtifact");
        using (var invalid = new HttpClient(new SigV4SigningHandler(
            new BasicAWSCredentials("hsr-local-proof-access", "wrong-local-proof-secret-synthetic-only"), RegionEndpoint.USEast1, "s3",
            new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }))
        { Timeout = TimeSpan.FromSeconds(10) })
        using (var denied = await invalid.GetAsync(address, token).ConfigureAwait(false))
            Check(denied.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, "InvalidSigningKeyCannotReadPrivateArtifact");
        Check(await fresh.ReadAsync(job with { Generation = 2 }, digest, token).ConfigureAwait(false) is null, "MissingGenerationRemainsMissing");
        var oversized = new byte[FixtureObservation.MaximumBytes + 1];
        var oversizedDigest = Convert.ToHexStringLower(SHA256.HashData(oversized));
        var oversizedAddress = new Uri(configuration.Endpoint, configuration.Bucket + "/" + S3ObservationArchive.ObjectKey(job, oversizedDigest));
        using (var put = await client.PutAsync(oversizedAddress, new ByteArrayContent(oversized), token).ConfigureAwait(false))
            if (put.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("S3_PROOF_OVERSIZED_CONTROL_WRITE_FAILED");
        await ExpectInvalidAsync(() => fresh.ReadAsync(job, oversizedDigest, token)).ConfigureAwait(false);
        Check(true, "OversizedObjectIsRejected");
        var corrupt = "owned-synthetic-corrupt-control"u8.ToArray();
        using (var put = await client.PutAsync(address, new ByteArrayContent(corrupt), token).ConfigureAwait(false))
            if (put.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("S3_PROOF_CORRUPTION_CONTROL_WRITE_FAILED");
        await ExpectInvalidAsync(() => fresh.FinalizeAsync(job, bytes, digest, token)).ConfigureAwait(false);
        var unchanged = await client.GetByteArrayAsync(address, token).ConfigureAwait(false);
        Check(corrupt.AsSpan().SequenceEqual(unchanged), "CorruptExistingObjectIsRejectedWithoutRewrite");
        return cases.ToArray();
    }

    private static async Task ExpectInvalidAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (ObservationArchiveException exception) when (exception.Failure == ObservationArchiveFailure.Invalid) { return; }
        throw new InvalidDataException("S3_PROOF_INVALID_ARTIFACT_ACCEPTED");
    }
}
