using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed partial record SyntheticSearchTaskBundle
{
    private static void ValidateExperiments(SyntheticSearchTaskManifest manifest, Dictionary<string, byte[]> files)
    {
        var documents = new HashSet<string>(StringComparer.Ordinal);
        var runs = new HashSet<string>(StringComparer.Ordinal);
        var reached = new List<bool>();
        for (var trial = 1; trial <= 4; trial++)
        {
            using var original = Parse(files, $"route-{trial}-recording.json");
            using var projection = Parse(files, $"route-{trial}.json");
            var root = original.RootElement;
            var receipt = Read<TraversalReceipt>(root.GetProperty("Receipt"));
            var summary = manifest.KeyboardTrials[trial - 1];
            var plan = SyntheticSearchTaskControl.KeyboardPlan(trial);
            var expectedKeys = new[] { "Tab" }.Concat("sample course".Select(character => character.ToString()))
                .Concat(trial <= 2 ? Array.Empty<string>() : ["Tab"]).Append(trial == 4 ? "Space" : "Enter").ToArray();
            Require(summary is not null && summary.Trial == trial && summary.Submission == (trial <= 2 ? "FieldEnter" : trial == 3 ? "ButtonEnter" : "ButtonSpace")
                && receipt.Schema == "hsr-traversal-v1" && receipt.Profile == FixtureObservation.Profile && receipt.Execution == TraversalExecution.Completed
                && receipt.Policy == TraversalPolicy.Allowed && receipt.Barrier == TraversalBarrier.NoneObserved && receipt.HasRecording
                && !receipt.TimelineTruncated && receipt.UnexecutedInstructions == 0 && receipt.ErrorCode is null
                && receipt.AttemptedKeystrokes == expectedKeys.Length && receipt.CompletedKeystrokes == expectedKeys.Length
                && receipt.StartedAtUtc != default && Guid.TryParseExact(receipt.RunId, "N", out _) && runs.Add(receipt.RunId)
                && summary.RunId == receipt.RunId && summary.Execution == receipt.Execution.ToString() && summary.Reachability == receipt.Reachability.ToString()
                && summary.AttemptedKeys == expectedKeys.Length && summary.CompletedKeys == expectedKeys.Length
                && !receipt.Observations.IsDefaultOrEmpty && receipt.Observations.Length <= 32
                && !receipt.Attempts.IsDefault && receipt.Attempts.Length == plan.Instructions.Length
                && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(receipt.Plan, ReaderWorkerWire.Json), JsonSerializer.SerializeToElement(plan, ReaderWorkerWire.Json)));
            var document = receipt.Observations[0].Snapshot.DocumentId;
            Require(documents.Add(document) && plan.Precondition.IsSatisfiedBy(receipt.Observations[0].Snapshot)
                && !plan.Destination.IsSatisfiedBy(receipt.Observations[0].Snapshot)
                && receipt.Observations.All(observation => observation.Snapshot.DocumentId == document && observation.Snapshot.CaptureEpoch == 1
                    && !observation.Snapshot.Nodes.IsDefaultOrEmpty && observation.Snapshot.Nodes.Length <= 2048)
                && receipt.Attempts.Select(attempt => attempt.Instruction).SequenceEqual(plan.Instructions)
                && receipt.Attempts.All(attempt => attempt.Step is { ErrorCode: null })
                && receipt.Timeline.Where(item => item.Kind == ReaderEventKind.InputAttempted).Select(item => item.Detail).SequenceEqual(expectedKeys)
                && receipt.Timeline.Where(item => item.Kind == ReaderEventKind.InputCompleted).Select(item => item.Detail).SequenceEqual(expectedKeys));
            var entry = receipt.Attempts[1].Step;
            Require(entry is
            {
                BrowserKeystrokes: 13, SearchInput:
                {
                    ValueVerified: true, CompletedCharacters: 13,
                    Profile: SyntheticSearchPolicy.Profile, Text: SyntheticSearchPolicy.Query
                }
            });
            ValidateGraph(Read<TraversalGraph>(projection.RootElement.GetProperty("Graph")), receipt);
            Require(JsonElement.DeepEquals(projection.RootElement.GetProperty("Receipt"), root.GetProperty("Receipt"))
                && JsonElement.DeepEquals(projection.RootElement.GetProperty("Recording"), root.GetProperty("Recording")));
            var goal = plan.Destination.IsSatisfiedBy(receipt.Observations[^1].Snapshot);
            Require(goal ? receipt.Reachability == TraversalReachability.Reached : receipt.Reachability == TraversalReachability.Unknown
                && !receipt.Observations.Any(observation => plan.Destination.IsSatisfiedBy(observation.Snapshot))
                && receipt.Attempts[^1].Step?.Verification == VerificationStatus.TimedOut);
            reached.Add(goal);
            Source(root.GetProperty("Environment"), manifest.Start.Job.Source);
            var recording = root.GetProperty("Recording");
            Require(recording.GetProperty("Stage").GetString() == "Complete" && recording.GetProperty("ErrorCode").ValueKind == JsonValueKind.Null);
            var alignment = recording.GetProperty("Alignment");
            Require(alignment.GetProperty("RunId").GetString() == receipt.RunId && alignment.GetProperty("DocumentId").GetString() == document
                && alignment.GetProperty("VideoSha256").GetString() == TraversalArchive.Hash(files[$"route-{trial}-media/original.webm"])
                && !alignment.GetProperty("Calibrated").GetBoolean() && alignment.GetProperty("Cues").GetArrayLength() == 0
                && alignment.GetProperty("ReasonCode").GetString() == "TRAVERSAL_RECORDING_FRAME_EVIDENCE_INVALID");
            Closed(recording.GetProperty("CaptureLifecycle"), recording.GetProperty("Shutdown"));
            Media(recording.GetProperty("Media"), files, $"route-{trial}-media/");
        }
        using var pointerOriginal = Parse(files, "pointer.json");
        var pointer = pointerOriginal.RootElement;
        var preparation = Read<SearchPointerPreparation>(pointer.GetProperty("Preparation"));
        var activation = Read<PointerTaskReceipt>(pointer.GetProperty("Activation"));
        Require(preparation is
        {
            ErrorCode: null, TimelineTruncated: false, ReaderStartedAtUtc: not null,
            Focus: { InputAttempted: true, InputCompleted: true, FocusObserved: true, ErrorCode: null, After: not null },
            Entry:
            {
                ErrorCode: null, InputMayHaveOccurred: false, BrowserKeystrokes: 13, SearchInput:
                {
                    ValueVerified: true, CompletedCharacters: 13,
                    Profile: SyntheticSearchPolicy.Profile, Text: SyntheticSearchPolicy.Query
                }
            }, AfterEntry: not null
        }
            && activation is { Attempted: true, Completed: true, GoalObserved: true, After: not null, ErrorCode: null }
            && Guid.TryParseExact(activation.RunId, "N", out _) && runs.Add(activation.RunId) && activation.RunId == manifest.PointerRunId
            && pointer.GetProperty("Stage").GetString() == "Complete" && pointer.GetProperty("ErrorCode").ValueKind == JsonValueKind.Null);
        using var pointerProjection = Parse(files, "pointer-graph.json");
        var pointerGraph = Read<SearchPointerGraph>(pointerProjection.RootElement);
        ValidatePointerGraph(pointerGraph, preparation, activation);
        var pointerDocument = preparation.Focus.Before.DocumentId;
        var destination = SyntheticSearchTaskControl.KeyboardPlan(1).Destination;
        Require(documents.Add(pointerDocument) && documents.Count == 5 && runs.Count == 5
            && pointerGraph.Edges.Length == 3 && pointerGraph.Edges.All(edge => edge.InputEvidenceComplete && !edge.InputUnconfirmed)
            && pointerGraph.Edges.Select(edge => edge.CompletedNativeCalls).SequenceEqual(new[] { 1, 13, 1 })
            && pointerGraph.Edges[1].KeyboardInputs.Select(key => key.Key).SequenceEqual("sample course".Select(character => character.ToString()))
            && pointerGraph.Nodes.All(node => node.Snapshot.DocumentId == pointerDocument && node.Snapshot.CaptureEpoch == 1)
            && !destination.IsSatisfiedBy(preparation.Focus.Before) && !destination.IsSatisfiedBy(preparation.AfterEntry)
            && !destination.IsSatisfiedBy(activation.Before) && destination.IsSatisfiedBy(activation.After));
        Source(pointer.GetProperty("Environment"), manifest.Start.Job.Source);
        Closed(pointer.GetProperty("Capture"), pointer.GetProperty("Shutdown"));
        Media(pointer.GetProperty("Media"), files, "pointer-media/");
        var observation = reached.All(value => value) ? "AllTestedKeyboardGoalsObserved" : reached.Any(value => value)
            ? "AKeyboardAlternativeReachedTheGoal" : "KeyboardGoalNotObservedAcrossFieldAndButtonAlternativesWithPointerGoalObserved";
        Require(manifest.Observation == observation);
    }

    private static void ValidateGraph(TraversalGraph graph, TraversalReceipt receipt)
    {
        Require(graph.Version == "execution-trace-v2" && !graph.Nodes.IsDefault && !graph.Edges.IsDefault && graph.UnresolvedAttempts.IsEmpty
            && graph.Nodes.Length == receipt.Observations.Length && graph.Edges.Length == receipt.Attempts.Length
            && graph.Nodes.Length == graph.Edges.Length + 1 && graph.Nodes.Select(node => node.ObservationId).Distinct(StringComparer.Ordinal).Count() == graph.Nodes.Length
            && graph.Edges.Select(edge => edge.AttemptId).Distinct(StringComparer.Ordinal).Count() == graph.Edges.Length);
        for (var index = 0; index < graph.Nodes.Length; index++)
        {
            var node = graph.Nodes[index]; var observation = receipt.Observations[index]; var snapshot = observation.Snapshot;
            var current = snapshot.Nodes.FirstOrDefault(item => item.Id == observation.CursorNodeId)
                ?? (snapshot.EffectiveFocusedNode?.Role != "RootWebArea" ? snapshot.EffectiveFocusedNode : null)
                ?? snapshot.Nodes.FirstOrDefault(item => item.Role == "heading") ?? snapshot.Nodes.FirstOrDefault();
            var label = current is null ? "No exposed node" : $"{current.Role}: {(current.Name.Length == 0 ? "(unnamed)" : current.Name)}";
            Require(node.ObservationId == observation.Id && node.DocumentId == snapshot.DocumentId && node.Fingerprint == observation.Fingerprint
                && node.Mode == observation.Mode && node.CursorNodeId == observation.CursorNodeId && node.FocusedNodeId == snapshot.FocusedNode?.Id
                && node.ActiveDescendantNodeId == snapshot.FocusedNode?.ActiveDescendantId && node.Label == label);
        }
        for (var index = 0; index < graph.Edges.Length; index++)
        {
            var edge = graph.Edges[index]; var attempt = receipt.Attempts[index]; var step = attempt.Step!;
            Require(edge.AttemptId == attempt.Id && edge.SourceObservationId == graph.Nodes[index].ObservationId
                && edge.ResultObservationId == graph.Nodes[index + 1].ObservationId && attempt.SourceObservationId == edge.SourceObservationId
                && attempt.ResultObservationId == edge.ResultObservationId && edge.Command == attempt.Instruction.Command
                && edge.Outcome == step.Outcome && edge.Verification == step.Verification && edge.InputMayHaveOccurred == step.InputMayHaveOccurred
                && edge.InputEvidenceComplete && !attempt.TimelineTruncated && edge.CompletedKeystrokes == step.BrowserKeystrokes
                && !attempt.Events.IsDefault && attempt.Events.SequenceEqual(receipt.Timeline.Where(item => item.CommandSequence == step.Sequence && item.Command == attempt.Instruction.Command)));
            ValidateKeys(edge.KeyboardInputs, attempt.Events, step.BrowserKeystrokes);
        }
    }

    private static void ValidateKeys(System.Collections.Immutable.ImmutableArray<TraversalKeyTrace> keys,
        System.Collections.Immutable.ImmutableArray<ReaderEvent> events, int completed)
    {
        Require(!keys.IsDefault && !events.IsDefault);
        var attempts = events.Where(item => item.Kind == ReaderEventKind.InputAttempted).ToArray();
        var completions = events.Where(item => item.Kind == ReaderEventKind.InputCompleted).ToArray();
        Require(keys.Length == completed && attempts.Length == completed && completions.Length == completed);
        for (var index = 0; index < completed; index++)
            Require(keys[index].Key == attempts[index].Detail && completions[index].Detail == attempts[index].Detail
                && keys[index].AttemptEventSequence == attempts[index].Sequence && keys[index].CompletionEventSequence == completions[index].Sequence
                && completions[index].Sequence > attempts[index].Sequence && (index + 1 == completed || completions[index].Sequence < attempts[index + 1].Sequence));
    }

    private static void ValidatePointerGraph(SearchPointerGraph graph, SearchPointerPreparation preparation, PointerTaskReceipt activation)
    {
        Require(!graph.Nodes.IsDefault && graph.Nodes.Length is 4 or 5 && !graph.Edges.IsDefault && graph.Edges.Length == 3);
        var snapshots = graph.Nodes.Length == 5
            ? new[] { preparation.Focus!.Before, preparation.Focus.After!, preparation.AfterEntry!, activation.Before, activation.After! }
            : new[] { preparation.Focus!.Before, preparation.Focus.After!, preparation.AfterEntry!, activation.After! };
        if (graph.Nodes.Length == 4) Require(Same(preparation.AfterEntry, activation.Before));
        for (var index = 0; index < snapshots.Length; index++)
            Require(graph.Nodes[index].Id == "p" + index && Same(graph.Nodes[index].Snapshot, snapshots[index]));
        var focus = graph.Edges[0]; var text = graph.Edges[1]; var activate = graph.Edges[2];
        Require(focus.Id == "focus" && focus.Source == "p0" && focus.Target == "p1" && focus.Mechanism == "NativeMouseFocus"
            && focus.KeyboardInputs.IsEmpty && text.Id == "text" && text.Source == "p1" && text.Target == "p2" && text.Mechanism == "ReaderKeyboardText"
            && activate.Id == "activate" && activate.Source == (graph.Nodes.Length == 5 ? "p3" : "p2")
            && activate.Target == "p" + (graph.Nodes.Length - 1) && activate.Mechanism == "NativeMouseActivation" && activate.KeyboardInputs.IsEmpty
            && graph.Edges.All(edge => edge.ErrorCode is null && edge.AttemptedNativeCalls == edge.CompletedNativeCalls && edge.InputEvidenceComplete && !edge.InputUnconfirmed)
            && graph.Edges.Select(edge => edge.CompletedNativeCalls).SequenceEqual(new[] { 1, 13, 1 }));
        var entryEvents = preparation.Timeline.Where(item => item.CommandSequence == preparation.Entry!.Sequence && item.Command == ReaderCommand.EnterSearchText).ToImmutableArray();
        ValidateKeys(text.KeyboardInputs, entryEvents, 13);
    }

    private static bool Same<T>(T left, T right) => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left, ReaderWorkerWire.Json), JsonSerializer.SerializeToElement(right, ReaderWorkerWire.Json));

    private static JsonDocument Parse(Dictionary<string, byte[]> files, string name)
    {
        Require(files.TryGetValue(name, out var bytes));
        return JsonDocument.Parse(bytes, new() { AllowDuplicateProperties = false });
    }

    private static void Source(JsonElement environment, SyntheticSearchTaskSource source)
    {
        var content = environment.GetProperty("Content");
        Require(content.GetProperty("ContentSha256").GetString() == source.Sha256
            && content.GetProperty("Fixture").GetString() == (source.Id == "search-working" ? "MockSearchWorkingV1" : "MockSearchKeyboardBlockedV1"));
    }

    private static void Closed(JsonElement capture, JsonElement shutdown)
    {
        var c = Read<CaptureStatus>(capture); var s = Read<BrowserShutdownStatus>(shutdown);
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
        var source = media.GetProperty("Source").EnumerateArray().ToArray();
        var precise = media.GetProperty("PreciseFrames").EnumerateArray().ToArray();
        var video = media.GetProperty("Video");
        var artifacts = media.GetProperty("Artifacts").EnumerateArray().ToArray();
        Require(source.Length is > 0 and <= 250 && precise.Length == source.Length && video.GetArrayLength() > 0
            && media.GetProperty("SourceClock").GetString() == "ChromiumCdpJsonDouble" && media.GetProperty("VideoFramesPerSecond").GetInt32() == 25
            && artifacts.Length == precise.Length + 2);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var staging = 0;
        foreach (var artifact in artifacts)
        {
            var name = artifact.GetProperty("File").GetString();
            Require(name is not null && names.Add(name));
            Require(files.TryGetValue(prefix + name, out var bytes) && artifact.GetProperty("Bytes").GetInt64() == bytes.Length
                && artifact.GetProperty("Sha256").GetString() == TraversalArchive.Hash(bytes));
            if (name == "original.webm") Require(bytes.AsSpan().StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }) && artifact.GetProperty("DecodedFrames").GetInt32() == video.GetArrayLength());
            else if (name.StartsWith("staging/", StringComparison.Ordinal))
            {
                Require(name.Length == 45 && name.EndsWith(".webm", StringComparison.Ordinal) && name.AsSpan(8, 32).ToString().All(char.IsAsciiHexDigitLower)
                    && bytes.AsSpan().StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }) && artifact.GetProperty("DecodedFrames").GetInt32() > 0);
                staging++;
            }
            else Require(bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }) && artifact.GetProperty("DecodedFrames").GetInt32() == 1);
        }
        Require(names.Contains("original.webm") && staging == 1);
        var sequences = new HashSet<int>();
        foreach (var frame in precise)
        {
            var sequence = frame.GetProperty("Sequence").GetInt32();
            var name = frame.GetProperty("File").GetString();
            var time = frame.GetProperty("PresentationUnixMilliseconds").GetDouble();
            Require(sequence is >= 1 and <= 250 && sequences.Add(sequence) && name == $"source-{sequence:D4}.jpg" && names.Contains(name)
                && double.IsFinite(time) && time > 0 && frame.GetProperty("Sha256").GetString() == TraversalArchive.Hash(files[prefix + name]));
            var linked = source.Where(item => item.GetProperty("File").GetString() == name).ToArray();
            Require(linked.Length == 1 && linked[0].GetProperty("PresentationUnixMilliseconds").GetDouble() == time && linked[0].GetProperty("Marker").ValueKind == JsonValueKind.Null);
        }
        var order = source.Select((frame, index) => (frame, index)).OrderBy(item => item.frame.GetProperty("PresentationUnixMilliseconds").GetDouble()).ThenBy(item => item.index).ToArray();
        Require(JsonElement.DeepEquals(media.GetProperty("SourcePresentationOrder"), JsonSerializer.SerializeToElement(order.Select(item => item.index)))
            && JsonElement.DeepEquals(media.GetProperty("SourceInPresentationOrder"), JsonSerializer.SerializeToElement(order.Select(item => item.frame))));
    }
}
