using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class FeatureProvenanceTests
{
    [Fact]
    public void DiagnosticHint_KeepsUnnamedSemanticsAndLinksActualFocusTransitionsWithoutAssigningFeatureSuccess()
    {
        var (discovery, receipt) = Example();
        var before = JsonSerializer.Serialize(receipt);
        var inventory = FeatureProvenance.Build(discovery, receipt);
        var feature = Assert.Single(inventory.Features);
        Assert.Equal(FeatureSourceBinding.Matched, feature.Binding);
        Assert.False(feature.HasExposedName);
        Assert.Equal("account-settings", feature.Candidate.Hint!.Value);
        Assert.Equal("", feature.Candidate.ObservedNode.Name);
        Assert.Equal(new[] { "o1", "o2", "o3" }, feature.ObservationIds);
        Assert.Equal(new[] { new FeaturePathRelation("a1", "o1", "o2"), new FeaturePathRelation("a2", "o2", "o3") }, feature.Relations);
        Assert.Equal(before, JsonSerializer.Serialize(receipt));
        Assert.Equal(TraversalReachability.Reached, receipt.Reachability);
        var report = FeatureReport.Render(discovery, receipt);
        Assert.Contains("button: (unnamed)", report);
        Assert.Contains("[a2](#a2)", report);
        Assert.Contains("Route verification above remains independent", report);
    }

    [Theory]
    [InlineData("discovery", FeatureSourceBinding.DiscoveryUnconfirmed)]
    [InlineData("empty", FeatureSourceBinding.NoSourceObservation)]
    [InlineData("document", FeatureSourceBinding.DocumentMismatch)]
    [InlineData("epoch", FeatureSourceBinding.EpochMismatch)]
    [InlineData("missing", FeatureSourceBinding.NodeMissing)]
    [InlineData("changed", FeatureSourceBinding.NodeChanged)]
    public void MissingForeignOrChangedSource_PreservesDiagnosticEvidenceWithoutPathLinks(string change, FeatureSourceBinding expected)
    {
        var (discovery, receipt) = Example();
        var source = receipt.Observations[0];
        switch (change)
        {
            case "discovery": discovery = discovery with { Candidates = [discovery.Candidates[0] with { StableDuringDiscovery = false }] }; break;
            case "empty": receipt = receipt with { Observations = [], Attempts = [] }; break;
            case "document": source = source with { Snapshot = source.Snapshot with { DocumentId = "foreign" } }; break;
            case "epoch": source = source with { Snapshot = source.Snapshot with { CaptureEpoch = 2 } }; break;
            case "missing": source = source with { Snapshot = source.Snapshot with { Nodes = [] } }; break;
            case "changed": source = source with { Snapshot = source.Snapshot with { Nodes = [source.Snapshot.Nodes[0] with { Name = "Different" }] } }; break;
        }
        if (change != "empty") receipt = receipt with { Observations = receipt.Observations.SetItem(0, source) };
        var feature = Assert.Single(FeatureProvenance.Build(discovery, receipt).Features);
        Assert.Equal(expected, feature.Binding);
        Assert.Null(feature.SourceObservationId);
        Assert.Empty(feature.ObservationIds);
        Assert.Empty(feature.Relations);
        Assert.Equal("account-settings", feature.Candidate.Hint!.Value);
    }

    [Fact]
    public void InterruptedRelatedAttempt_RetainsNullResultAndDoesNotInventAnEdgeOrVerifiedFeature()
    {
        var (discovery, receipt) = Example();
        receipt = receipt with
        {
            Observations = [.. receipt.Observations.Take(2)],
            Attempts = receipt.Attempts.SetItem(1, receipt.Attempts[1] with { ResultObservationId = null, Step = null }),
            Reachability = TraversalReachability.Unknown,
            Execution = TraversalExecution.Cancelled,
            StopReason = TraversalStopReason.Cancelled
        };
        var feature = Assert.Single(FeatureProvenance.Build(discovery, receipt).Features);
        Assert.Null(feature.Relations[^1].ResultObservationId);
        Assert.DoesNotContain("o3", feature.ObservationIds);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
        Assert.Empty(TraversalProjection.Build(receipt).Edges.Where(e => e.AttemptId == "a2"));
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-node")]
    [InlineData("oversize")]
    [InlineData("foreign-node")]
    [InlineData("editable")]
    [InlineData("protected")]
    [InlineData("hint-state")]
    [InlineData("hint-source")]
    [InlineData("hint-length")]
    public void InvalidDiagnosticContracts_AreRejectedBeforeEvidenceProjection(string invalid)
    {
        var (discovery, receipt) = Example();
        var candidate = discovery.Candidates[0];
        discovery = invalid switch
        {
            "duplicate-id" => discovery with { Candidates = [candidate, candidate with { ObservedNode = candidate.ObservedNode with { Id = "another" } }] },
            "duplicate-node" => discovery with { Candidates = [candidate, candidate with { Id = "another" }] },
            "oversize" => discovery with { Candidates = [.. Enumerable.Range(0, 65).Select(i => candidate with { Id = "f" + i, ObservedNode = candidate.ObservedNode with { Id = "n" + i } })] },
            "foreign-node" => discovery with { Candidates = [candidate with { ObservedNode = candidate.ObservedNode with { DocumentId = "foreign" } }] },
            "editable" => discovery with { Candidates = [candidate with { ObservedNode = candidate.ObservedNode with { Editability = new("richtext", SemanticSource.ChromiumAccessibility) } }] },
            "protected" => discovery with { Candidates = [candidate with { ObservedNode = candidate.ObservedNode with { Protection = new(true, SemanticSource.NativeHtmlProperty) } }] },
            "hint-state" => discovery with { Candidates = [candidate with { HintState = DiagnosticHintState.Absent }] },
            "hint-source" => discovery with { Candidates = [candidate with { Hint = candidate.Hint! with { Source = (DiagnosticHintSource)99 } }] },
            _ => discovery with { Candidates = [candidate with { Hint = candidate.Hint! with { Value = new string('x', 129) } }] }
        };
        Assert.Equal("FEATURE_DISCOVERY_INVALID", Assert.Throws<ArgumentException>(() => FeatureProvenance.Build(discovery, receipt)).Message.Split(" (Parameter", StringSplitOptions.None)[0]);
    }

    [Fact]
    public async Task InjectedDiagnosticHint_IsEncodedAndWriterRetainsBoundInventoryWithoutOverwriting()
    {
        var (discovery, receipt) = Example();
        discovery = discovery with
        {
            Truncated = true,
            Candidates = [discovery.Candidates[0] with { Hint = new("[leak](https://example.com)|<script>x</script>\nrow", DiagnosticHintSource.DeclaredFeatureAttribute) }]
        };
        var folder = Path.GetFullPath(Path.Combine("artifacts", "feature-writer", Guid.NewGuid().ToString("N")));
        await TraversalArtifactWriter.WriteAsync(folder, 1, receipt, features: discovery);
        var report = await File.ReadAllTextAsync(Path.Combine(folder, "route-1.md"));
        Assert.DoesNotContain("<script>", report);
        Assert.DoesNotContain("[leak](https://", report);
        Assert.Contains("&lt;script&gt;", report);
        Assert.Contains("&#124;", report);
        Assert.Contains("truncated: True", report);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "route-1.json")));
        Assert.Equal(receipt.RunId, json.RootElement.GetProperty("Features").GetProperty("RunId").GetString());
        Assert.Equal(discovery.DiscoveryId, json.RootElement.GetProperty("Features").GetProperty("Discovery").GetProperty("DiscoveryId").GetString());
        await Assert.ThrowsAsync<IOException>(() => TraversalArtifactWriter.WriteAsync(folder, 1, receipt, features: discovery));
        Assert.Equal(report, await File.ReadAllTextAsync(Path.Combine(folder, "route-1.md")));
    }

    [Fact]
    public async Task InvalidInventory_PreventsPublishingAnUnrelatedTraceBundle()
    {
        var (discovery, receipt) = Example();
        var folder = Path.GetFullPath(Path.Combine("artifacts", "feature-writer", Guid.NewGuid().ToString("N")));
        discovery = discovery with { Candidates = [discovery.Candidates[0], discovery.Candidates[0]] };
        await Assert.ThrowsAsync<ArgumentException>(() => TraversalArtifactWriter.WriteAsync(folder, 1, receipt, features: discovery));
        Assert.Empty(Directory.EnumerateFiles(folder));
    }

    internal static (FeatureDiscovery Discovery, TraversalReceipt Receipt) Example()
    {
        var node = new AccessibleNode("control", "button", "", DocumentId: "document", FrameId: "main");
        AccessibilitySnapshot Snapshot(bool focused) => new("document", [node with { IsFocused = focused }], []) { CaptureEpoch = 1 };
        var observations = new[]
        {
            new TraversalObservation("o1", Snapshot(false), ReaderMode.Browse, null, "before", 0),
            new TraversalObservation("o2", Snapshot(true), ReaderMode.Focus, node.Id, "focus", 1),
            new TraversalObservation("o3", Snapshot(false), ReaderMode.Browse, null, "after", 2)
        };
        var attempts = new[]
        {
            new TraversalAttempt("a1", "o1", "o2", new(ReaderCommand.Tab), new ReaderStep(1, ReaderCommand.Tab, ReaderMode.Focus, ReaderOutcome.Observed, "button", node.Id, node.Id, 1), [], false, 0, 1),
            new TraversalAttempt("a2", "o2", "o3", new(ReaderCommand.Activate), new ReaderStep(2, ReaderCommand.Activate, ReaderMode.Browse, ReaderOutcome.Observed, "Done", null, null, 1, Verification: VerificationStatus.Satisfied), [], false, 1, 2)
        };
        var plan = new TraversalPlan("Authored test", new("heading", "Start"), new("heading", "Done"), [new(ReaderCommand.Tab), new(ReaderCommand.Activate)]);
        var receipt = new TraversalReceipt("hsr-traversal-v1", ScreenReaderSession.Profile, plan, new(),
            TraversalExecution.Completed, TraversalReachability.Reached, TraversalBarrier.NoneObserved, TraversalPolicy.Allowed,
            TraversalStopReason.RouteCompleted, [.. observations], [.. attempts], [], false, 0);
        return (new("document", 1, [new("feature-1", node, new("account-settings", DiagnosticHintSource.DeclaredFeatureAttribute),
            DiagnosticHintState.Observed, true)], false, []), receipt);
    }
}
