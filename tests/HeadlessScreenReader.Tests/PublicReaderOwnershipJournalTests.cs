using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PublicReaderOwnershipJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hsr-public-ownership-" + Guid.NewGuid().ToString("N"));
    private static readonly Guid Organization = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Session = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Image = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private PublicReaderOwnershipJournal Create() => new(_root);
    private static string IntentPath(PublicReaderOwnershipJournal journal, PublicReaderOwnership intent) => Path.Combine(journal.IntentsDirectory, intent.Owner.ToString("N") + ".json");
    private static string RemovalPath(PublicReaderOwnershipJournal journal, PublicReaderOwnership intent) => Path.Combine(journal.RemovalsDirectory, intent.Owner.ToString("N") + ".json");
    private static Task<PublicReaderOwnership> Begin(PublicReaderOwnershipJournal journal) => journal.BeginAsync(Organization, Session, PublicReaderWorkerContractTests.Source(), "podman", Image);
    public void Dispose()
    {
        var resolved = Path.GetFullPath(_root);
        const string prefix = "hsr-public-ownership-";
        var name = Path.GetFileName(resolved);
        if (resolved != _root || !name.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
            throw new InvalidOperationException("TEST_DIRECTORY_SCOPE_INVALID");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }

    [Fact]
    public async Task PersistedIntentBindsPublicSourceTenantSessionAndFiveExactNames()
    {
        var journal = Create(); var intent = await Begin(journal);
        var recovered = Assert.Single(Create().ReadPending());
        Assert.Equal(intent, recovered); Assert.NotEqual(Guid.Empty, recovered.Owner);
        Assert.Equal(Organization, recovered.OrganizationId); Assert.Equal(Session, recovered.SessionId);
        Assert.Equal("hsr-public-reader-ownership-v1", recovered.Schema); Assert.Equal("podman", recovered.Runtime); Assert.Equal(Image, recovered.Image);
        Assert.Equal("https://screenreader.privensic.com/ci/v1/source", recovered.Source.TargetUrl);
        Assert.Equal("hsr-approved-public-readonly-v1", recovered.Source.Profile);
        var prefix = "hsr-public-" + recovered.Owner.ToString("N");
        Assert.Equal(new[] { prefix + "-worker", prefix + "-proxy", prefix + "-fence", prefix + "-private", prefix + "-egress" },
            new[] { recovered.Worker, recovered.Proxy, recovered.Fence, recovered.InternalNetwork, recovered.EgressNetwork });
        Assert.Equal(intent.Owner.ToString("N") + ".json", Path.GetFileName(Assert.Single(Directory.GetFiles(journal.IntentsDirectory))));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(IntentPath(journal, intent)));
        Assert.False(json.RootElement.TryGetProperty("Prefix", out _)); Assert.False(json.RootElement.TryGetProperty("FixtureSha256", out _));
        Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
    }

    [Fact]
    public void ExclusiveLockPreventsCompetingSupervisorsUntilReleased()
    {
        var journal = Create();
        using (journal.AcquireExclusive()) Assert.Throws<IOException>(() => Create().AcquireExclusive());
        using var next = Create().AcquireExclusive(); Assert.True(next.CanWrite);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("session")]
    [InlineData("source")]
    [InlineData("runtime")]
    [InlineData("image-tag")]
    [InlineData("image-case")]
    [InlineData("image-length")]
    [InlineData("image-newline")]
    public async Task InvalidBeginCannotCreateAnOwnershipIntent(string scenario)
    {
        var journal = Create(); var source = PublicReaderWorkerContractTests.Source();
        var image = scenario switch { "image-tag" => "latest", "image-case" => "sha256:" + new string('A', 64), "image-length" => "sha256:a", "image-newline" => Image + "\n", _ => Image };
        await Assert.ThrowsAsync<InvalidDataException>(() => journal.BeginAsync(scenario == "organization" ? Guid.Empty : Organization,
            scenario == "session" ? Guid.Empty : Session, scenario == "source" ? source with { Profile = "Foundation" } : source,
            scenario == "runtime" ? "shell" : "podman", image));
        Assert.Empty(Directory.GetFiles(journal.IntentsDirectory)); Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
    }

    [Fact]
    public async Task AlreadyCanceledBeginLeavesNoPartialIntent()
    {
        var journal = Create(); using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.BeginAsync(Organization, Session, PublicReaderWorkerContractTests.Source(), "podman", Image, canceled.Token));
        Assert.Empty(Directory.GetFiles(journal.IntentsDirectory));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("owner")]
    [InlineData("organization")]
    [InlineData("session")]
    [InlineData("source-null")]
    [InlineData("source-target")]
    [InlineData("runtime")]
    [InlineData("image")]
    public async Task ChangedPersistedAuthorityCannotBecomeRecoveryPermission(string scenario)
    {
        var journal = Create(); var intent = await Begin(journal);
        var invalid = scenario switch
        {
            "schema" => intent with { Schema = "hsr-native-ownership-v1" },
            "owner" => intent with { Owner = Guid.Empty },
            "organization" => intent with { OrganizationId = Guid.Empty },
            "session" => intent with { SessionId = Guid.Empty },
            "source-null" => intent with { Source = null! },
            "source-target" => intent with { Source = intent.Source with { TargetUrl = "https://127.0.0.1/" } },
            "runtime" => intent with { Runtime = "shell" },
            _ => intent with { Image = "latest" }
        };
        await File.WriteAllBytesAsync(IntentPath(journal, intent), JsonSerializer.SerializeToUtf8Bytes(invalid));
        Assert.Equal("PUBLIC_READER_OWNERSHIP_INVALID", Assert.Throws<InvalidDataException>(() => journal.ReadPending()).Message);
        Assert.Single(Directory.GetFiles(journal.IntentsDirectory)); Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("null")]
    public async Task AmbiguousOrIncompleteInventoryCannotBeReturnedPartly(string scenario)
    {
        var journal = Create(); var first = await Begin(journal); var second = await Begin(journal);
        var firstBytes = await File.ReadAllBytesAsync(IntentPath(journal, first));
        var valid = await File.ReadAllTextAsync(IntentPath(journal, second));
        var invalid = scenario switch
        {
            "malformed" => "{\"Owner\":",
            "unknown" => "{\"Unexpected\":true," + valid[1..],
            "duplicate" => "{\"Runtime\":\"docker\"," + valid[1..],
            "missing" => valid.Replace("\"Runtime\":\"podman\",", "", StringComparison.Ordinal),
            _ => "null"
        };
        await File.WriteAllTextAsync(IntentPath(journal, second), invalid);
        Assert.Equal("PUBLIC_READER_OWNERSHIP_INVALID", Assert.Throws<InvalidDataException>(() => journal.ReadPending()).Message);
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(IntentPath(journal, first)));
        Assert.Equal(2, Directory.GetFiles(journal.IntentsDirectory).Length); Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
    }

    [Theory]
    [InlineData("filename")]
    [InlineData("extension")]
    public async Task ResourceIdentityMustMatchItsExactIntentFilename(string scenario)
    {
        var journal = Create(); var intent = await Begin(journal);
        File.Move(IntentPath(journal, intent), Path.Combine(journal.IntentsDirectory, scenario == "filename" ? Guid.NewGuid().ToString("N") + ".json" : intent.Owner.ToString("N") + ".txt"));
        Assert.Equal(scenario == "filename" ? "PUBLIC_READER_OWNERSHIP_IDENTITY_MISMATCH" : "PUBLIC_READER_OWNERSHIP_INVALID", Assert.Throws<InvalidDataException>(() => journal.ReadPending()).Message);
        Assert.Single(Directory.GetFiles(journal.IntentsDirectory)); Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("at-limit")]
    [InlineData("over-limit")]
    public async Task JournalPinsExactRecordByteBounds(string scenario)
    {
        var journal = Create(); var intent = await Begin(journal); var json = await File.ReadAllTextAsync(IntentPath(journal, intent));
        var bytes = scenario == "empty" ? [] : Encoding.UTF8.GetBytes(json.PadRight(scenario == "at-limit" ? 16384 : 16385));
        await File.WriteAllBytesAsync(IntentPath(journal, intent), bytes);
        if (scenario == "at-limit") Assert.Equal(intent, Assert.Single(journal.ReadPending()));
        else Assert.Equal("PUBLIC_READER_OWNERSHIP_SIZE_INVALID", Assert.Throws<InvalidDataException>(() => journal.ReadPending()).Message);
        Assert.Single(Directory.GetFiles(journal.IntentsDirectory)); Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
    }

    [Fact]
    public async Task AbsenceAcknowledgementSurvivesRetirementAndRetryWithoutRewritingHistory()
    {
        var journal = Create(); var intent = await Begin(journal);
        await journal.RecordRemovalAsync(intent, recovered: true);
        Assert.Empty(Create().ReadPending());
        var path = Assert.Single(Directory.GetFiles(journal.RemovalsDirectory)); var bytes = await File.ReadAllBytesAsync(path);
        var proof = JsonSerializer.Deserialize<PublicReaderRemoval>(bytes, ObservationStore.Json)!;
        Assert.Equal("hsr-public-reader-removal-v1", proof.Schema); Assert.Equal(intent, proof.Intent); Assert.NotEqual(default, proof.ObservedAt);
        Assert.True(proof.ContainersAbsenceConfirmed); Assert.True(proof.NetworksAbsenceConfirmed); Assert.True(proof.RecoveredAfterRestart);
        await Create().RecordRemovalAsync(intent, recovered: false);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path)); Assert.Empty(journal.ReadPending());
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("session")]
    [InlineData("source")]
    [InlineData("image")]
    public async Task CallerCannotRetireDifferentPersistedAuthority(string scenario)
    {
        var journal = Create(); var intent = await Begin(journal); var before = await File.ReadAllBytesAsync(IntentPath(journal, intent));
        var changed = scenario switch
        {
            "organization" => intent with { OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333") },
            "session" => intent with { SessionId = Guid.Parse("33333333-3333-3333-3333-333333333333") },
            "source" => intent with { Source = intent.Source with { TargetUrl = "https://screenreader.privensic.com/different" } },
            _ => intent with { Image = "sha256:" + new string('b', 64) }
        };
        Assert.Equal("PUBLIC_READER_OWNERSHIP_IDENTITY_MISMATCH", (await Assert.ThrowsAsync<InvalidDataException>(() => journal.RecordRemovalAsync(changed, false))).Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(IntentPath(journal, intent))); Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
    }

    [Fact]
    public async Task MissingIntentCannotBeInventedByAnAbsenceAcknowledgement()
    {
        var journal = Create(); var intent = await Begin(journal); File.Delete(IntentPath(journal, intent));
        Assert.Equal("PUBLIC_READER_OWNERSHIP_MISSING", (await Assert.ThrowsAsync<InvalidDataException>(() => journal.RecordRemovalAsync(intent, false))).Message);
        Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("organization")]
    [InlineData("containers")]
    [InlineData("networks")]
    [InlineData("time")]
    [InlineData("partial")]
    public async Task InvalidAcknowledgementCannotDiscardThePendingIntent(string scenario)
    {
        var journal = Create(); var intent = await Begin(journal); var before = await File.ReadAllBytesAsync(IntentPath(journal, intent));
        var proof = new PublicReaderRemoval("hsr-public-reader-removal-v1", intent, DateTimeOffset.Parse("2026-11-02T12:00:00Z"), true, true, true);
        proof = scenario switch
        {
            "schema" => proof with { Schema = "other" },
            "organization" => proof with { Intent = intent with { OrganizationId = Guid.Empty } },
            "containers" => proof with { ContainersAbsenceConfirmed = false },
            "networks" => proof with { NetworksAbsenceConfirmed = false },
            "time" => proof with { ObservedAt = default },
            _ => proof
        };
        await File.WriteAllBytesAsync(RemovalPath(journal, intent), scenario == "partial" ? [123] : JsonSerializer.SerializeToUtf8Bytes(proof));
        Assert.Equal(scenario == "partial" ? "PUBLIC_READER_OWNERSHIP_INVALID" : "PUBLIC_READER_REMOVAL_IDENTITY_MISMATCH", (await Assert.ThrowsAsync<InvalidDataException>(() => journal.RecordRemovalAsync(intent, false))).Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(IntentPath(journal, intent))); Assert.Single(Directory.GetFiles(journal.RemovalsDirectory));
    }

    [Fact]
    public async Task InterruptedIntentRetirementReconcilesTheExistingExactAcknowledgement()
    {
        var journal = Create(); var intent = await Begin(journal);
        var proof = new PublicReaderRemoval("hsr-public-reader-removal-v1", intent, DateTimeOffset.Parse("2026-11-02T12:00:00Z"), true, true, true);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(proof); await File.WriteAllBytesAsync(RemovalPath(journal, intent), bytes);
        Assert.Equal(intent, Assert.Single(Create().ReadPending()));
        await Create().RecordRemovalAsync(intent, recovered: false);
        Assert.Empty(journal.ReadPending()); Assert.Equal(bytes, await File.ReadAllBytesAsync(RemovalPath(journal, intent)));
    }
}
