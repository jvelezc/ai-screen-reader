using System.IO.Compression;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Transport identity only. The original embedded package retains its own unmodified scope and uncertainty.</summary>
public sealed record MockTaskCollectionReceipt(string Version, MockTaskStart Start, string ManifestSha256,
    string ArchiveSha256, int AuthorizedBoundaries)
{
    public const string Schema = "hsr-isolated-mock-task-collection-v1";
}

/// <summary>Bounded originals and a source/job-bound receipt. No file extraction, tenant access or evidence decision.</summary>
public sealed record MockTaskCollectionBundle(MockTaskCollectionReceipt Receipt, byte[] Originals)
{
    public const int MaximumOriginalBytes = 25 * 1024 * 1024;
    public const int MaximumArchiveBytes = MaximumOriginalBytes + 1024 * 1024;
    public const int MaximumBundleBytes = MaximumArchiveBytes + 64 * 1024;
    private const string ManifestName = "mock-task-manifest.json";

    internal static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition)
    {
        if (!condition) throw new BrowserOperationException("MOCK_TASK_BUNDLE_UNCONFIRMED");
    }

    public void Validate(MockTaskStart expected)
    {
        expected.Validate();
        Require(Receipt is not null && Receipt.Version == MockTaskCollectionReceipt.Schema && Receipt.Start == expected
            && Receipt.AuthorizedBoundaries == MockTaskControl.Boundaries.Length && Originals is { Length: > 0 and <= MaximumArchiveBytes }
            && Receipt.ArchiveSha256 == TraversalArchive.Hash(Originals));
        var files = ReadFiles(Originals, MaximumArchiveBytes, 256);
        Require(files.TryGetValue("mock-task-execution.json", out var binding) && binding.Length <= MockTaskControl.MaximumFrameBytes);
        var collectedFor = JsonSerializer.Deserialize<MockTaskStart>(binding, ReaderWorkerWire.Json);
        Require(collectedFor == expected);
        Require(files.TryGetValue(ManifestName, out var manifest) && manifest.Length <= 128 * 1024
            && TraversalArchive.Hash(manifest) == Receipt.ManifestSha256);
        using var doc = JsonDocument.Parse(manifest!, new() { AllowDuplicateProperties = false });
        var root = doc.RootElement;
        string[] fields = ["Schema", "CreatedAtUtc", "Source", "KeyboardRuns", "PointerRunId", "PointerGoalObservation", "Observation",
            "ProducerSha256", "ProducerVersion", "Runtime", "Platform", "Viewport", "Files", "Scope", "VideoAlignment", "WebsiteEvidenceAssessment",
            "ExternalAssistiveTechnologyComparison", "LegalCaseDetermined"];
        Require(root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(fields.Order(StringComparer.Ordinal))
            && root.GetProperty("CreatedAtUtc").GetDateTimeOffset() != default
            && root.GetProperty("ProducerSha256").GetString() is { Length: 64 } producer && producer.All(char.IsAsciiHexDigitLower)
            && new[] { "ProducerVersion", "Runtime", "Platform" }.All(name => root.GetProperty(name).GetString() is { Length: > 0 and <= 500 } text && !text.Any(char.IsControl)));
        Require(root.GetProperty("Schema").GetString() == "hsr-mock-task-recordings-v1"
            && root.GetProperty("Scope").GetString() == "LocalEmbeddedMockTaskExperiments"
            && root.GetProperty("VideoAlignment").GetString() == "UnverifiedWithoutCalibrationMarkers"
            && root.GetProperty("WebsiteEvidenceAssessment").GetString() == "NotAssessed"
            && root.GetProperty("ExternalAssistiveTechnologyComparison").GetString() == "NotPerformed"
            && root.GetProperty("LegalCaseDetermined").ValueKind == JsonValueKind.False
            && root.GetProperty("Viewport").GetString() == "320x240");
        var source = root.GetProperty("Source");
        var fixture = expected.Job.Source.Id switch
        {
            "checkout-working" => "MockCheckoutWorkingV1",
            "checkout-keyboard-blocked" => "MockCheckoutKeyboardBlockedV1",
            "control-named" => "MockNamedControlV1",
            "control-unnamed" => "MockUnnamedControlV1",
            _ => ""
        };
        Require(source.GetProperty("Id").GetString() == expected.Job.Source.Id
            && source.GetProperty("Fixture").GetString() == fixture
            && source.GetProperty("SourceSha256").GetString() == expected.Job.Source.Sha256
            && source.GetProperty("Heading").GetString() == expected.Job.Source.ExpectedHeading()
            && source.GetProperty("GoalName").GetString() == expected.Job.Source.ExpectedGoal());
        Require(files.TryGetValue("fixture-source.txt", out var html) && TraversalArchive.Hash(html) == expected.Job.Source.Sha256);
        var inventory = root.GetProperty("Files").EnumerateArray().ToArray();
        Require(inventory.Length == files.Count - 1 && inventory.Length > 0
            && inventory.Select(f => f.GetProperty("Path").GetString()).Distinct(StringComparer.Ordinal).Count() == inventory.Length);
        foreach (var item in inventory)
        {
            var path = item.GetProperty("Path").GetString();
            Require(path is not null && path != ManifestName && files.TryGetValue(path, out _));
            var bytes = files[path!];
            Require(item.GetProperty("Bytes").GetInt64() == bytes.Length && item.GetProperty("Sha256").GetString() == TraversalArchive.Hash(bytes));
        }
        ValidateExperiments(root, files, expected.Job.Source);
    }

    private static void ValidateExperiments(JsonElement manifest, Dictionary<string, byte[]> files, MockTaskSource source)
    {
        var runs = manifest.GetProperty("KeyboardRuns").EnumerateArray().ToArray();
        Require(runs.Length == 3);
        var runIds = new HashSet<string>(StringComparer.Ordinal);
        var documents = new HashSet<string>(StringComparer.Ordinal);
        var goals = new List<bool>();
        var goal = new ObservationExpectation("heading", source.ExpectedGoal(), MustBeFocused: true);
        for (var i = 1; i <= 3; i++)
        {
            var key = i == 3 ? "Space" : "Enter";
            var summary = runs[i - 1];
            Require(summary.GetProperty("Route").GetInt32() == i && summary.GetProperty("Key").GetString() == key
                && summary.GetProperty("Execution").GetString() == "Completed" && summary.GetProperty("HasVideo").GetBoolean()
                && summary.GetProperty("AttemptedKeys").GetInt32() == 2 && summary.GetProperty("CompletedKeys").GetInt32() == 2
                && summary.GetProperty("ErrorCode").ValueKind == JsonValueKind.Null);
            using var doc = JsonDocument.Parse(files[$"route-{i}.json"], new() { AllowDuplicateProperties = false });
            var row = doc.RootElement;
            var r = JsonSerializer.Deserialize<TraversalReceipt>(row.GetProperty("Receipt"), ReaderWorkerWire.Json);
            Require(r is not null && r.Schema == "hsr-traversal-v1" && r.Profile == FixtureObservation.Profile
                && r.Execution == TraversalExecution.Completed && r.Policy == TraversalPolicy.Allowed
                && r.Barrier == TraversalBarrier.NoneObserved && r.HasRecording && !r.TimelineTruncated && r.UnexecutedInstructions == 0
                && r.CompletedKeystrokes == 2 && r.AttemptedKeystrokes == 2 && r.StartedAtUtc != default
                && Guid.TryParseExact(r.RunId, "N", out _) && r.RunId == summary.GetProperty("RunId").GetString() && runIds.Add(r.RunId)
                && !r.Observations.IsDefaultOrEmpty && r.Observations.Length <= 32);
            var plan = new TraversalPlan($"{source.Id}: keyboard {key}", new("heading", source.ExpectedHeading()), goal,
                key == "Enter" ? [new(ReaderCommand.Tab), new(ReaderCommand.Activate)]
                    : [new(ReaderCommand.Tab), new(ReaderCommand.EnterFocusMode), new(ReaderCommand.PressKey, "Space")]);
            Require(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(r!.Plan, ReaderWorkerWire.Json), JsonSerializer.SerializeToElement(plan, ReaderWorkerWire.Json))
                && documents.Add(r.Observations[0].Snapshot.DocumentId) && !goal.IsSatisfiedBy(r.Observations[0].Snapshot)
                && r.Observations.All(o => o.Snapshot.DocumentId == r.Observations[0].Snapshot.DocumentId
                    && !o.Snapshot.Nodes.IsDefaultOrEmpty && o.Snapshot.Nodes.Length <= 2048 && o.Snapshot.CaptureEpoch == 1)
                && r.Timeline.Where(e => e.Kind == ReaderEventKind.InputCompleted).Select(e => e.Detail).SequenceEqual(new[] { "Tab", key }));
            Require(row.GetProperty("Receipt").GetProperty("AttemptedKeystrokes").GetInt32() == r.AttemptedKeystrokes
                && row.GetProperty("Receipt").GetProperty("CompletedKeystrokes").GetInt32() == r.CompletedKeystrokes);
            var graph = JsonSerializer.Deserialize<TraversalGraph>(row.GetProperty("Graph"), ReaderWorkerWire.Json);
            Require(graph is not null && !graph.Nodes.IsDefault && graph.Nodes.Length == r.Observations.Length
                && graph.Nodes.Select(n => n.ObservationId).Distinct(StringComparer.Ordinal).Count() == graph.Nodes.Length
                && graph.Nodes.All(n => r.Observations.Any(o => o.Id == n.ObservationId && o.Snapshot.DocumentId == n.DocumentId && o.Fingerprint == n.Fingerprint))
                && !graph.Edges.IsDefault && graph.Edges.Length == r.Attempts.Length
                && graph.Edges.Select(e => e.AttemptId).Distinct(StringComparer.Ordinal).Count() == graph.Edges.Length
                && graph.Edges.All(e => r.Attempts.Any(a => a.Id == e.AttemptId && a.SourceObservationId == e.SourceObservationId
                    && a.ResultObservationId == e.ResultObservationId && a.Instruction.Command == e.Command
                    && a.Step is { } step && step.Outcome == e.Outcome && step.Verification == e.Verification)) && graph.UnresolvedAttempts.IsEmpty);
            var observed = goal.IsSatisfiedBy(r.Observations[^1].Snapshot);
            Require(r.Reachability.ToString() == summary.GetProperty("Reachability").GetString()
                && (observed ? r.Reachability == TraversalReachability.Reached : r.Reachability == TraversalReachability.Unknown
                    && !r.Observations.Any(o => goal.IsSatisfiedBy(o.Snapshot)) && r.Attempts[^1].Step?.Verification == VerificationStatus.TimedOut));
            goals.Add(observed);
            var recording = row.GetProperty("Recording");
            Require(recording.GetProperty("Stage").GetString() == "Complete" && recording.GetProperty("ErrorCode").ValueKind == JsonValueKind.Null);
            var alignment = recording.GetProperty("Alignment");
            Require(alignment.GetProperty("RunId").GetString() == r.RunId && alignment.GetProperty("DocumentId").GetString() == r.Observations[0].Snapshot.DocumentId
                && alignment.GetProperty("VideoSha256").GetString() == TraversalArchive.Hash(files[$"route-{i}-media/original.webm"])
                && alignment.GetProperty("Calibrated").ValueKind == JsonValueKind.False && alignment.GetProperty("Cues").GetArrayLength() == 0
                && alignment.GetProperty("ReasonCode").GetString() == "TRAVERSAL_RECORDING_FRAME_EVIDENCE_INVALID");
            Closed(recording.GetProperty("CaptureLifecycle"), recording.GetProperty("Shutdown"));
            Media(recording.GetProperty("Media"), files, $"route-{i}-media/");
        }
        using var pointerDoc = JsonDocument.Parse(files["pointer.json"], new() { AllowDuplicateProperties = false });
        var pointer = pointerDoc.RootElement;
        var p = JsonSerializer.Deserialize<PointerTaskReceipt>(pointer.GetProperty("Receipt"), ReaderWorkerWire.Json);
        Require(p is { Attempted: true, Completed: true, GoalObserved: true, ErrorCode: null, After: not null }
            && p.StartedAtUtc != default && Guid.TryParseExact(p.RunId, "N", out _) && runIds.Add(p.RunId)
            && documents.Add(p.Before.DocumentId) && p.Before.DocumentId == p.After.DocumentId
            && !goal.IsSatisfiedBy(p.Before) && goal.IsSatisfiedBy(p.After)
            && manifest.GetProperty("PointerRunId").GetString() == p.RunId && manifest.GetProperty("PointerGoalObservation").GetString() == "Observed"
            && pointer.GetProperty("ErrorCode").ValueKind == JsonValueKind.Null);
        var raw = pointer.GetProperty("Receipt");
        Require(raw.GetProperty("Mechanism").GetString() == "Pointer" && raw.GetProperty("KeyboardInputs").GetInt32() == 0
            && raw.GetProperty("InputMayHaveOccurred").GetBoolean() && raw.GetProperty("GoalObservation").GetString() == "Observed");
        Closed(pointer.GetProperty("Capture"), pointer.GetProperty("Shutdown"));
        Media(pointer.GetProperty("Media"), files, "pointer-media/");
        var expected = goals.All(v => v) ? "AllTestedKeyboardGoalsObserved" : goals.All(v => !v)
            ? "RepeatedKeyboardGoalNotObservedWithPointerGoalObserved" : "IncompleteOrMixedResults";
        Require(expected != "IncompleteOrMixedResults" && manifest.GetProperty("Observation").GetString() == expected);
    }

    private static void Closed(JsonElement capture, JsonElement shutdown)
    {
        var c = JsonSerializer.Deserialize<CaptureStatus>(capture, ReaderWorkerWire.Json);
        var s = JsonSerializer.Deserialize<BrowserShutdownStatus>(shutdown, ReaderWorkerWire.Json);
        Require(c is { Phase: CapturePhase.Closed, EventsTruncated: false }
            && s is
            {
                Phase: BrowserShutdownPhase.Closed, ActiveCalls: 0, ErrorCode: null, ProcessExitConfirmed: true,
                CollectorStopAcknowledged: true, ContextCallSettled: true, BrowserCallSettled: true, ProcessCallSettled: true,
                TransportDisposed: true, TransportCallSettled: true, CollectorCallSettled: true
            });
    }

    private static void Media(JsonElement media, Dictionary<string, byte[]> files, string prefix)
    {
        Require(media.GetProperty("Source").GetArrayLength() > 0 && media.GetProperty("Video").GetArrayLength() > 0);
        var artifacts = media.GetProperty("Artifacts").EnumerateArray().Where(a => a.GetProperty("File").GetString() == "original.webm").ToArray();
        Require(artifacts.Length == 1);
        Require(files.TryGetValue(prefix + "original.webm", out var bytes) && bytes.AsSpan().StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }));
        Require(artifacts[0].GetProperty("Bytes").GetInt64() == bytes.Length && artifacts[0].GetProperty("Sha256").GetString() == TraversalArchive.Hash(bytes)
            && artifacts[0].GetProperty("DecodedFrames").GetInt32() == media.GetProperty("Video").GetArrayLength());
    }

    public byte[] Encode(MockTaskStart expected)
    {
        Validate(expected);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using (var file = zip.CreateEntry("receipt.json", CompressionLevel.NoCompression).Open())
                JsonSerializer.Serialize(file, Receipt, ReaderWorkerWire.Json);
            using (var file = zip.CreateEntry("originals.zip", CompressionLevel.NoCompression).Open()) file.Write(Originals);
        }
        Require(output.Length <= MaximumBundleBytes);
        return output.ToArray();
    }

    public static MockTaskCollectionBundle Decode(byte[] bytes, MockTaskStart expected)
    {
        expected.Validate();
        var files = ReadFiles(bytes, MaximumBundleBytes, 2);
        Require(files.Count == 2 && files.ContainsKey("receipt.json") && files.ContainsKey("originals.zip") && files["receipt.json"].Length <= 4096);
        var receipt = JsonSerializer.Deserialize<MockTaskCollectionReceipt>(files["receipt.json"], ReaderWorkerWire.Json)
            ?? throw new BrowserOperationException("MOCK_TASK_BUNDLE_UNCONFIRMED");
        var result = new MockTaskCollectionBundle(receipt, files["originals.zip"]);
        result.Validate(expected);
        return result;
    }

    private static Dictionary<string, byte[]> ReadFiles(byte[] bytes, int maximumBytes, int maximumFiles)
    {
        Require(bytes is { Length: > 0 } && bytes.Length <= maximumBytes);
        using var input = new MemoryStream(bytes, false); using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        Require(zip.Entries.Count > 0 && zip.Entries.Count <= maximumFiles);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            Require(entry.FullName.Length is > 0 and <= 240 && entry.FullName.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.')
                && entry.FullName.Split('/').All(part => part is not ("" or "." or ".."))
                && names.Add(entry.FullName) && entry.Length is > 0 && entry.Length <= maximumBytes);
            total = checked(total + entry.Length); Require(total <= maximumBytes);
            using var source = entry.Open(); using var output = new MemoryStream(); var buffer = new byte[8192];
            while (true)
            {
                var count = source.Read(buffer); if (count == 0) break;
                Require(output.Length + count <= entry.Length); output.Write(buffer, 0, count);
            }
            Require(output.Length == entry.Length); result.Add(entry.FullName, output.ToArray());
        }
        return result;
    }
}
