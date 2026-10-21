using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;
using Microsoft.Playwright;

if (args.Contains("--help", StringComparer.Ordinal))
{
    Console.WriteLine("HeadlessScreenReader. Usage: --private-pixels | --headed-private-pixels | --private-viewer | --synthetic-handoff | --headed-synthetic-handoff | --headed-session | --controller | --owned-scan | --replay | --features | --traversal-recording | --recording-sync | --capture-probe | [--traversal | --widgets | --observe-live | --text | --frames | --ranges | --composites | --tables | --paragraphs | --multiselect | --typeahead] [--headed] | --reference-template | --reference-case <id> | --assess-reference <file>");
    foreach (var item in ReferenceValidation.Cases) Console.WriteLine($"  {item.Id}: {item.Task}");
    return 0;
}
if (args.SequenceEqual(["--reference-template"]))
{
    Console.WriteLine(JsonSerializer.Serialize(ReferenceValidation.CreateWorksheet(), ReferenceValidation.JsonOptions));
    return 0;
}
if (args.Length == 2 && args[0] == "--assess-reference")
{
    try
    {
        using var input = File.OpenRead(args[1]);
        var assessment = ReferenceValidation.Assess(ReferenceValidation.ReadWorksheet(input));
        Console.WriteLine(JsonSerializer.Serialize(assessment, ReferenceValidation.JsonOptions));
        return assessment.SelectedCasesReviewed ? 0 : 3;
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
    {
        Console.Error.WriteLine("REFERENCE_WORKSHEET_INVALID_OR_UNREADABLE");
        return 2;
    }
}
var referenceCase = args.Length == 2 && args[0] == "--reference-case"
    ? ReferenceValidation.FindCase(args[1]) : null;
if (referenceCase is null && (args.Any(arg => arg is not "--private-pixels" and not "--headed-private-pixels" and not "--private-viewer" and not "--synthetic-handoff" and not "--headed-synthetic-handoff" and not "--headed-session" and not "--controller" and not "--owned-scan" and not "--replay" and not "--features" and not "--traversal-recording" and not "--recording-sync" and not "--capture-probe" and not "--headed" and not "--traversal" and not "--widgets" and not "--observe-live" and not "--text" and not "--frames" and not "--ranges" and not "--composites" and not "--tables" and not "--paragraphs" and not "--multiselect" and not "--typeahead")
    || args.Count(arg => arg is "--private-pixels" or "--headed-private-pixels" or "--private-viewer" or "--synthetic-handoff" or "--headed-synthetic-handoff" or "--headed-session" or "--controller" or "--owned-scan" or "--replay" or "--features" or "--traversal-recording" or "--recording-sync" or "--capture-probe" or "--traversal" or "--widgets" or "--observe-live" or "--text" or "--frames" or "--ranges" or "--composites" or "--tables" or "--paragraphs" or "--multiselect" or "--typeahead") > 1
    || args.Any(arg => arg is "--private-pixels" or "--headed-private-pixels" or "--private-viewer" or "--synthetic-handoff" or "--headed-synthetic-handoff" or "--headed-session" or "--controller" or "--owned-scan" or "--replay" or "--features" or "--traversal-recording" or "--capture-probe" or "--recording-sync") && args.Length != 1))
{
    Console.Error.WriteLine("Invalid arguments or unknown reference case. Use --help; only bundled fixtures are supported.");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var json = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
try
{
    if (args.Contains("--private-pixels", StringComparer.Ordinal) || args.Contains("--headed-private-pixels", StringComparer.Ordinal))
        return await PrivatePixelDemo.RunAsync(args.Contains("--headed-private-pixels", StringComparer.Ordinal), cancellation.Token);
    if (args.Contains("--private-viewer", StringComparer.Ordinal))
        return await PrivateViewerDemo.RunAsync(cancellation.Token);
    if (args.Contains("--synthetic-handoff", StringComparer.Ordinal) || args.Contains("--headed-synthetic-handoff", StringComparer.Ordinal))
        return await SyntheticHandoffDemo.RunAsync(args.Contains("--headed-synthetic-handoff", StringComparer.Ordinal), cancellation.Token);
    if (args.Contains("--headed-session", StringComparer.Ordinal))
        return await HeadedSessionDemo.RunAsync(cancellation.Token);
    if (args.Contains("--controller", StringComparer.Ordinal))
        return await ControllerDemo.RunAsync(cancellation.Token);
    if (args.Contains("--owned-scan", StringComparer.Ordinal))
        return await OwnedScanDemo.RunAsync(cancellation.Token);
    if (args.Contains("--replay", StringComparer.Ordinal))
        return await ReplayDemo.RunAsync(cancellation.Token);
    if (args.Contains("--features", StringComparer.Ordinal))
        return await FeatureDemo.RunAsync(cancellation.Token);
    if (args.Contains("--traversal-recording", StringComparer.Ordinal))
        return await TraversalRecordingDemo.RunAsync(Path.Combine("artifacts", "traversal-recording"), cancellation.Token);
    if (args.Contains("--recording-sync", StringComparer.Ordinal))
    {
        var receipt = await RecordingSynchronizationDemo.RunAsync(Path.Combine("artifacts", "recording-sync"), cancellation.Token);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Synchronization = receipt.Synchronization?.Verification.ToString() ?? "Unknown",
            receipt.ErrorCode,
            receipt.Stage,
            MaxConservativeErrorMilliseconds = receipt.Synchronization?.Markers.Max(m => m.ConservativeAbsoluteErrorMilliseconds),
            Report = Path.Combine(receipt.Folder, "index.md")
        }, json));
        return receipt.ErrorCode is null && receipt.Synchronization?.Verification == HeadlessScreenReader.Domain.SynchronizationVerification.Verified ? 0 : 3;
    }
    if (args.Contains("--capture-probe", StringComparer.Ordinal))
    {
        var receipt = await ChromiumFixtureBrowser.RunCapturePrivacyProbeAsync(Path.Combine("artifacts", "capture-probe"), cancellation.Token);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            CaptureProbeVerified = receipt.Verified,
            receipt.ErrorCode,
            receipt.Stage,
            OriginalSegments = receipt.Segments.Length,
            InspectedArtifacts = receipt.Artifacts.Length,
            DecodedFrames = receipt.Artifacts.Sum(a => a.Pixels.Frames),
            Report = Path.Combine(receipt.Folder, "index.md")
        }, json));
        return receipt.Verified ? 0 : 3;
    }
    if (args.Contains("--traversal", StringComparer.Ordinal))
        return await TraversalDemo.RunAsync(args.Contains("--headed", StringComparer.Ordinal), cancellation.Token);
    await using var browser = await ChromiumFixtureBrowser.LaunchAsync(
        headed: referenceCase is not null || args.Contains("--headed", StringComparer.Ordinal),
        cancellationToken: cancellation.Token,
        fixture: referenceCase?.Fixture
            ?? (args.Contains("--typeahead", StringComparer.Ordinal) ? BundledFixture.Typeahead
                : args.Contains("--multiselect", StringComparer.Ordinal) ? BundledFixture.Multiselect
                : args.Contains("--paragraphs", StringComparer.Ordinal) ? BundledFixture.Paragraphs
                : args.Contains("--tables", StringComparer.Ordinal) ? BundledFixture.Tables
                : args.Contains("--composites", StringComparer.Ordinal) ? BundledFixture.Composites
                : args.Contains("--ranges", StringComparer.Ordinal) ? BundledFixture.Ranges
                : args.Contains("--frames", StringComparer.Ordinal) ? BundledFixture.Frames
                : args.Contains("--text", StringComparer.Ordinal) ? BundledFixture.Text
                : args.Contains("--observe-live", StringComparer.Ordinal) ? BundledFixture.LiveRegions
                : args.Contains("--widgets", StringComparer.Ordinal) ? BundledFixture.Widgets : BundledFixture.Foundation));
    if (referenceCase is not null)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Mode = "ManualReference",
            ReferenceStatus = "PendingOperator",
            Profile = ScreenReaderSession.Profile,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Host = RuntimeInformation.OSDescription,
            Runtime = RuntimeInformation.FrameworkDescription,
            browser.Environment,
            Case = referenceCase,
            Instructions = "Use NVDA and physical keyboard input in the fixture window. Close it when finished. Session expires after ten minutes. Record observations in your worksheet; no reader commands or speech capture run automatically."
        }, ReferenceValidation.JsonOptions));
        try
        {
            await browser.WaitForCloseAsync(TimeSpan.FromMinutes(10), cancellation.Token);
            Console.WriteLine("Reference window closed. NVDA observations still require operator review.");
            return 0;
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("REFERENCE_SESSION_EXPIRED; no NVDA result was inferred.");
            return 3;
        }
    }
    using var session = new ScreenReaderSession(browser, maxCommands: 50);
    async Task<ReaderStep> Execute(ReaderCommand command, string? key = null, ObservationExpectation? goal = null)
    {
        var step = await session.ExecuteAsync(command, key, cancellation.Token, goal);
        Console.WriteLine(JsonSerializer.Serialize(new { Profile = ScreenReaderSession.Profile, Step = step }, json));
        return step;
    }
    if (args.Contains("--typeahead", StringComparer.Ordinal))
    {
        await Execute(ReaderCommand.Tab);
        var first = await Execute(ReaderCommand.Typeahead, "PE", new("option", "Peaches", MustBeFocused: true, Selected: false));
        var repeated = await Execute(ReaderCommand.Typeahead, "pe", new("option", "Pears", MustBeFocused: true, Selected: false));
        var absent = await Execute(ReaderCommand.Typeahead, "zz");
        var rawDenied = await Execute(ReaderCommand.PressKey, "p");
        await Execute(ReaderCommand.Tab);
        var roving = await Execute(ReaderCommand.Typeahead, "pep", new("option", "Peppers", MustBeFocused: true, Selected: false));
        var matched = first.Verification == VerificationStatus.Satisfied && repeated.Verification == VerificationStatus.Satisfied
            && first.CursorNodeId != repeated.CursorNodeId && first.BrowserKeystrokes == 2 && repeated.BrowserKeystrokes == 2;
        var selection = new[] { first, repeated, absent }.All(step =>
            step.ListboxSelection?.Options.Where(o => o.Selection?.Value == true).Select(o => o.Name).SequenceEqual(["Apples"]) == true);
        var unknown = absent.Outcome == ReaderOutcome.Unknown && absent.Typeahead?.PrefixMatchesObservedFocus == false && absent.BarrierCode is null;
        var rovingObserved = roving.Verification == VerificationStatus.Satisfied && roving.FocusedNodeId == roving.CursorNodeId && roving.ActiveDescendantNodeId is null;
        var policy = rawDenied.Outcome == ReaderOutcome.Denied && rawDenied.BrowserKeystrokes == 0;
        foreach (var item in session.Timeline) Console.WriteLine(JsonSerializer.Serialize(new { Event = item }, json));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            PrefixAndResetVerified = matched,
            SelectionSeparate = selection,
            NoMatchRemainsUnknown = unknown,
            RovingFocusVerified = rovingObserved,
            RawPrintableKeyDenied = policy,
            browser.Environment
        }, json));
        return matched && selection && unknown && rovingObserved && policy ? 0 : 1;
    }
    if (args.Contains("--multiselect", StringComparer.Ordinal))
    {
        var initial = await browser.CaptureAsync(cancellation.Token);
        await Execute(ReaderCommand.NextListbox);
        await Execute(ReaderCommand.NextOption);
        await Execute(ReaderCommand.NextOption);
        var noFocus = await Execute(ReaderCommand.Activate);
        var afterBrowse = await browser.CaptureAsync(cancellation.Token);
        var readOnly = initial.FocusedNode?.Id == afterBrowse.FocusedNode?.Id
            && initial.Nodes.Where(n => n.Role == "option").Select(n => (n.Id, n.Selected))
                .SequenceEqual(afterBrowse.Nodes.Where(n => n.Role == "option").Select(n => (n.Id, n.Selected)))
            && !session.Timeline.Any(e => e.Kind == ReaderEventKind.InputAttempted);
        var owner = initial.Nodes.Single(n => n.Role == "listbox" && n.Name == "Fruit");
        var apple = initial.Nodes.Single(n => n.Role == "option" && n.Name == "Apples");
        var pear = initial.Nodes.Single(n => n.Role == "option" && n.Name == "Pears");
        ObservationExpectation Goal(params string[] ids) => new("option", "Pears", MustBeFocused: true,
            ExposedSelection: new(owner.Id, owner.DocumentId!, owner.FrameId!, [.. ids]));
        await Execute(ReaderCommand.Tab);
        var moved = await Execute(ReaderCommand.PressKey, "ArrowDown");
        var selected = await Execute(ReaderCommand.Activate, goal: Goal(apple.Id, pear.Id));
        var deselected = await Execute(ReaderCommand.Activate, goal: Goal(apple.Id));
        await Execute(ReaderCommand.PressKey, "End");
        var disabled = await Execute(ReaderCommand.Activate);
        var source = selected.ListboxSelection?.MultiSelectable == new MultiSelectableObservation(true, SemanticSource.ChromiumAccessibility)
            && selected.ListboxSelection.Options.All(o => o.Selection?.Source == SemanticSource.ChromiumAccessibility);
        var scoped = selected.ListboxSelection is { ExposedOptionsOnly: true, Options.Length: 3 }
            && !initial.Nodes.Any(n => n.Name == "Hidden selected fruit");
        var focus = noFocus.Outcome == ReaderOutcome.FocusRequired && moved.FocusedNodeId == owner.Id
            && moved.ActiveDescendantNodeId == pear.Id && moved.CursorNodeId == pear.Id;
        var toggled = selected.Verification == VerificationStatus.Satisfied && deselected.Verification == VerificationStatus.Satisfied
            && selected.ListboxSelection!.Options.Count(o => o.Selection?.Value == true) == 2
            && deselected.ListboxSelection!.Options.Count(o => o.Selection?.Value == true) == 1;
        var blocked = disabled.Outcome == ReaderOutcome.Blocked && disabled.BrowserKeystrokes == 0;
        foreach (var item in session.Timeline) Console.WriteLine(JsonSerializer.Serialize(new { Event = item }, json));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            SourceTaggedSelectionObserved = source,
            ExposedSelectionScopePreserved = scoped,
            ReadOnlyBrowseObserved = readOnly,
            ActualFocusRequired = focus,
            MultipleSelectionAndDeselectionVerified = toggled,
            DisabledOptionBlockedWithoutInput = blocked,
            browser.Environment
        }, json));
        return source && scoped && readOnly && focus && toggled && blocked ? 0 : 1;
    }
    if (args.Contains("--paragraphs", StringComparer.Ordinal))
    {
        var before = await browser.CaptureAsync(cancellation.Token);
        var first = await Execute(ReaderCommand.NextParagraph);
        var current = await Execute(ReaderCommand.ReadCurrentParagraph);
        var boundary = await Execute(ReaderCommand.PreviousParagraph);
        var hidden = await Execute(ReaderCommand.NextParagraph);
        var unicode = await Execute(ReaderCommand.NextParagraph);
        var control = await Execute(ReaderCommand.NextParagraph);
        var after = await browser.CaptureAsync(cancellation.Token);
        var joined = first.Announcement == "Read international text with linked words."
            && current.Announcement == first.Announcement;
        var sources = first.Passage?.Fragments.Length > 1
            && first.Passage.Fragments.All(fragment => before.Nodes.Any(n => n.Id == fragment.NodeId && n.Text?.Text == fragment.Text));
        var bounded = boundary.Outcome == ReaderOutcome.NoMatch && boundary.CursorNodeId == first.CursorNodeId;
        var excluded = hidden.Announcement == "Visible only." && control.Outcome == ReaderOutcome.Unsupported && control.Passage is null;
        var exact = unicode.Announcement == "Unicode é👩‍💻\nnext.";
        var noInput = before.FocusedNode?.Id == after.FocusedNode?.Id && !session.Timeline.Any(e => e.Kind == ReaderEventKind.InputAttempted);
        foreach (var item in session.Timeline) Console.WriteLine(JsonSerializer.Serialize(new { Event = item }, json));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ParagraphTextJoined = joined,
            SourceFragmentsPreserved = sources,
            BoundariesPreserved = bounded,
            HiddenAndEditableExcluded = excluded,
            ExactUnicodeAndBreaksObserved = exact,
            ReadOnlyBrowseObserved = noInput,
            browser.Environment
        }, json));
        return joined && sources && bounded && excluded && exact && noInput ? 0 : 1;
    }
    if (args.Contains("--tables", StringComparer.Ordinal))
    {
        var initial = await browser.CaptureAsync(cancellation.Token);
        var table = await Execute(ReaderCommand.NextTable);
        await Execute(ReaderCommand.NextCell);
        await Execute(ReaderCommand.NextColumn);
        var apples = await Execute(ReaderCommand.NextRow);
        var pears = await Execute(ReaderCommand.NextRow);
        var boundary = await Execute(ReaderCommand.NextRow);
        var previous = await Execute(ReaderCommand.PreviousRow);
        await Execute(ReaderCommand.NextTable);
        var unknown = await Execute(ReaderCommand.NextCell);
        var final = await browser.CaptureAsync(cancellation.Token);
        var headers = apples.Table is { } context && context.RowHeaders.Select(h => h.Name).SequenceEqual(["Apples"])
            && context.ColumnHeaders.Select(h => h.Name).SequenceEqual(["Price"]);
        var moved = table.Table?.Geometry is { RowCount: 3, ColumnCount: 2 }
            && pears.Announcement.StartsWith("$4, cell", StringComparison.Ordinal)
            && previous.CursorNodeId == apples.CursorNodeId;
        var bounded = boundary.Outcome == ReaderOutcome.NoMatch && boundary.CursorNodeId == pears.CursorNodeId;
        var missing = unknown.Table is { RowHeaders.IsEmpty: true, ColumnHeaders.IsEmpty: true }
            && unknown.Announcement.Contains("column header unknown", StringComparison.Ordinal);
        var noInput = initial.FocusedNode?.Id == final.FocusedNode?.Id
            && !session.Timeline.Any(e => e.Kind == ReaderEventKind.InputAttempted);
        foreach (var item in session.Timeline) Console.WriteLine(JsonSerializer.Serialize(new { Event = item }, json));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            TableHeadersObserved = headers,
            CellNavigationObserved = moved,
            BoundariesPreserved = bounded,
            MissingHeadersRemainUnknown = missing,
            ReadOnlyBrowseObserved = noInput,
            browser.Environment
        }, json));
        return headers && moved && bounded && missing && noInput ? 0 : 1;
    }
    if (args.Contains("--composites", StringComparer.Ordinal))
    {
        await Execute(ReaderCommand.NextTree);
        await Execute(ReaderCommand.NextTreeItem);
        var initial = await browser.CaptureAsync(cancellation.Token);
        var readOnly = !session.Timeline.Any(item => item.Kind == ReaderEventKind.InputAttempted);
        var collapsed = !initial.Nodes.Any(node => node.Name is "Design" or "Notes" or "Details content");
        await Execute(ReaderCommand.Tab);
        var expanded = await Execute(ReaderCommand.PressKey, "ArrowRight",
            new("treeitem", "Projects", MustBeFocused: true, Expanded: true));
        var child = await Execute(ReaderCommand.PressKey, "ArrowDown",
            new("treeitem", "Design", MustBeFocused: true, Selected: false));
        var opened = await Execute(ReaderCommand.Activate,
            goal: new("treeitem", "Design", MustBeFocused: true, Selected: true));
        await Execute(ReaderCommand.Tab);
        var tabFocus = await Execute(ReaderCommand.PressKey, "ArrowRight",
            new("tab", "Details", MustBeFocused: true, Selected: false));
        var selected = await Execute(ReaderCommand.Activate,
            goal: new("tab", "Details", MustBeFocused: true, Selected: true));
        var final = await browser.CaptureAsync(cancellation.Token);
        var hierarchy = child.Context?.HierarchyLevel is { IsValid: true, Value: 2 }
            && child.Context.ExposedPeers?.Count == 2 && child.FocusedNodeId != child.CursorNodeId;
        var treeVerified = expanded.Verification == VerificationStatus.Satisfied
            && child.Verification == VerificationStatus.Satisfied && opened.Verification == VerificationStatus.Satisfied
            && final.Nodes.Any(node => node.Role == "StaticText" && node.Name == "Opened Design");
        var tabsVerified = tabFocus.Verification == VerificationStatus.Satisfied
            && selected.Verification == VerificationStatus.Satisfied
            && final.Nodes.Any(node => node.Role == "tabpanel" && node.Name == "Details");
        var hidden = collapsed && !final.Nodes.Any(node => node.Name == "Overview content");
        foreach (var item in session.Timeline) Console.WriteLine(JsonSerializer.Serialize(new { Event = item }, json));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ReadOnlyBrowseObserved = readOnly,
            TreeHierarchyObserved = hierarchy,
            TreeActivationVerified = treeVerified,
            ManualTabSelectionVerified = tabsVerified,
            HiddenContentExcluded = hidden,
            browser.Environment
        }, json));
        return readOnly && hierarchy && treeVerified && tabsVerified && hidden ? 0 : 1;
    }
    if (args.Contains("--ranges", StringComparer.Ordinal))
    {
        var browsed = await Execute(ReaderCommand.NextRange);
        var noInput = !session.Timeline.Any(item => item.Kind == ReaderEventKind.InputAttempted);
        var focusRequired = await Execute(ReaderCommand.Activate);
        await Execute(ReaderCommand.Tab);
        var changed = await Execute(ReaderCommand.PressKey, "ArrowRight",
            new("slider", "Volume", MustBeFocused: true, RangeValue: 50));
        var minimum = await Execute(ReaderCommand.PressKey, "Home");
        var maximum = await Execute(ReaderCommand.PressKey, "End");
        await Execute(ReaderCommand.Tab);
        var fractional = await Execute(ReaderCommand.PressKey, "ArrowRight",
            new("slider", "Level", MustBeFocused: true, RangeValue: 0.5));
        await Execute(ReaderCommand.Tab);
        var completed = await Execute(ReaderCommand.Activate,
            goal: new("progressbar", "Transfer", RangeValue: 200));
        var rangeSnapshot = await browser.CaptureAsync(cancellation.Token);
        var navigated = browsed.Range?.Value == 40 && noInput && focusRequired.Outcome == ReaderOutcome.FocusRequired;
        var sliderVerified = changed.Verification == VerificationStatus.Satisfied
            && fractional.Verification == VerificationStatus.Satisfied
            && minimum.Range?.Value == 0 && maximum.Range?.Value == 100;
        var progressVerified = completed.Verification == VerificationStatus.Satisfied;
        var unknown = rangeSnapshot.Nodes.Any(node => node.Role == "progressbar" && node.Name == "Waiting"
            && node.Range is { Value: null });
        foreach (var item in session.Timeline) Console.WriteLine(JsonSerializer.Serialize(new { Event = item }, json));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            RangeNavigationObserved = navigated,
            SliderValuesVerified = sliderVerified,
            ProgressValueVerified = progressVerified,
            UnknownProgressPreserved = unknown,
            browser.Environment
        }, json));
        return navigated && sliderVerified && progressVerified && unknown ? 0 : 1;
    }
    if (args.Contains("--frames", StringComparer.Ordinal))
    {
        await Execute(ReaderCommand.DocumentStart);
        await Execute(ReaderCommand.NextFrame);
        var child = await Execute(ReaderCommand.NextHeading);
        var nested = await Execute(ReaderCommand.NextHeading);
        var focused = false;
        for (var attempt = 0; attempt < 15; attempt++)
        {
            var step = await Execute(ReaderCommand.Tab);
            if (step.Announcement != "Child action, button") continue;
            focused = step.Frame?.ParentFrameId is not null && step.CursorNodeId == step.FocusedNodeId;
            break;
        }
        if (!focused) return 1;
        var activated = await Execute(ReaderCommand.Activate, goal: new("StaticText", "Child complete"));
        var frameSnapshot = await browser.CaptureAsync(cancellation.Token);
        var verified = activated.Verification == VerificationStatus.Satisfied;
        var coverage = frameSnapshot.Frames.Any(frame => frame.Status == FrameObservationStatus.Excluded)
            && frameSnapshot.Frames.Any(frame => frame.Status == FrameObservationStatus.Unsupported);
        var observed = child.Announcement == "Child heading, heading level 2" && nested.Announcement == "Nested heading, heading level 3";
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ChildFrameObserved = observed,
            ChildFocusObserved = focused,
            ChildActionVerified = verified,
            IncompleteFrameCoverageReported = coverage,
            frameSnapshot.Frames
        }, json));
        return observed && focused && verified && coverage ? 0 : 1;
    }
    if (args.Contains("--text", StringComparer.Ordinal))
    {
        await Execute(ReaderCommand.DocumentStart);
        var first = await Execute(ReaderCommand.NextCharacter);
        var second = await Execute(ReaderCommand.NextCharacter);
        var back = await Execute(ReaderCommand.PreviousCharacter);
        await Execute(ReaderCommand.NextWord);
        await Execute(ReaderCommand.NextLine);
        var noInput = !session.Timeline.Any(item => item.Kind == ReaderEventKind.InputAttempted);
        var reversed = first.TextRange == back.TextRange && first.TextRange != second.TextRange;
        Console.WriteLine(JsonSerializer.Serialize(new
        { TextNavigationObserved = reversed, ReaderDispatchedNoInput = noInput, LineMeaning = "Newline-delimited AX text runs; visual wraps unavailable." }, json));
        return reversed && noInput ? 0 : 1;
    }
    if (args.Contains("--observe-live", StringComparer.Ordinal))
    {
        var focused = false;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Execute(ReaderCommand.Tab);
            if ((await browser.CaptureAsync(cancellation.Token)).FocusedNode?.Name != "Start timed update") continue;
            focused = true;
            break;
        }
        if (!focused) return 1;
        await Execute(ReaderCommand.Activate);
        var observed = false;
        var noInput = true;
        var terminal = ObservationStreamStatus.Observed;
        await foreach (var batch in session.ObserveChangesAsync(new()
        { Duration = TimeSpan.FromSeconds(3), MaxCaptures = 20, PollInterval = TimeSpan.FromMilliseconds(25) }, cancellation.Token))
        {
            Console.WriteLine(JsonSerializer.Serialize(new { Profile = ScreenReaderSession.Profile, Observation = batch }, json));
            observed |= batch.Events.Any(item => item.Kind == ReaderEventKind.LiveRegionChanged && item.Detail == "Timed complete");
            noInput &= !batch.Events.Any(item => item.Kind == ReaderEventKind.InputAttempted);
            terminal = batch.Status;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { IdleChangeObserved = observed, ObserverDispatchedNoInput = noInput, TerminalStatus = terminal }, json));
        if (terminal == ObservationStreamStatus.Cancelled) return 130;
        return observed && noInput && terminal == ObservationStreamStatus.CaptureLimitReached ? 0 : 1;
    }
    if (args.Contains("--widgets", StringComparer.Ordinal))
    {
        async Task<bool> TabTo(string role, string name)
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var step = await Execute(ReaderCommand.Tab);
                if (step.Outcome != ReaderOutcome.Observed) return false;
                var focus = (await browser.CaptureAsync(cancellation.Token)).FocusedNode;
                if (focus?.Role == role && focus.Name == name) return true;
            }
            return false;
        }
        if (!await TabTo("button", "Review preferences")) return 1;
        if ((await Execute(ReaderCommand.Activate, goal: new("dialog", "Review preferences")))
            .Verification != VerificationStatus.Satisfied) return 1;
        if ((await Execute(ReaderCommand.Escape,
            goal: new("button", "Review preferences", MustBeFocused: true)))
            .Verification != VerificationStatus.Satisfied) return 1;
        if (!await TabTo("checkbox", "Accept terms")) return 1;
        if ((await Execute(ReaderCommand.Activate, goal: new("checkbox", "Accept terms", Checked: "true")))
            .Verification != VerificationStatus.Satisfied) return 1;
        if (!await TabTo("radio", "Standard")) return 1;
        if ((await Execute(ReaderCommand.PressKey, "ArrowRight",
            new("radio", "Express", MustBeFocused: true, Checked: "true")))
            .Verification != VerificationStatus.Satisfied) return 1;
        if (!await TabTo("combobox", "Delivery region")) return 1;
        if ((await Execute(ReaderCommand.PressKey, "ArrowDown",
            new("option", "North", MustBeFocused: true, Selected: true)))
            .Verification != VerificationStatus.Satisfied) return 1;
        if ((await Execute(ReaderCommand.PressKey, "ArrowDown",
            new("option", "South", MustBeFocused: true, Selected: true)))
            .Verification != VerificationStatus.Satisfied) return 1;
        if ((await Execute(ReaderCommand.Activate,
            goal: new("combobox", "Delivery region", MustBeFocused: true, Expanded: false)))
            .Verification != VerificationStatus.Satisfied) return 1;
        if (!await TabTo("button", "Save preferences")) return 1;
        var saved = await Execute(ReaderCommand.Activate,
            goal: new("heading", "Preferences saved", MustBeFocused: true));
        if (!await TabTo("button", "Continue to payment")) return 1;
        var blocked = await Execute(ReaderCommand.Activate, goal: new("heading", "Payment"));
        foreach (var item in session.Timeline)
            Console.WriteLine(JsonSerializer.Serialize(new { Event = item }, json));
        var widgetGoalReached = saved.Verification == VerificationStatus.Satisfied;
        var blockedRoute = blocked.Outcome == ReaderOutcome.Blocked
            && blocked.Verification == VerificationStatus.Blocked && blocked.BrowserKeystrokes == 0;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            FixtureGoalReached = widgetGoalReached,
            BlockedRouteObserved = blockedRoute,
            session.TimelineTruncated,
            Limitations = (await browser.CaptureAsync(cancellation.Token)).Limitations,
            Assessment = "One synthetic route was observed disabled; no claim about other routes or real AT."
        }, json));
        return widgetGoalReached && blockedRoute && !session.TimelineTruncated ? 0 : 1;
    }
    var commands = new[]
    {
        ReaderCommand.NextHeading,
        ReaderCommand.NextButton,
        ReaderCommand.Activate,
        ReaderCommand.Tab,
        ReaderCommand.Tab,
        ReaderCommand.Activate
    };
    foreach (var command in commands)
    {
        var step = await Execute(command);
        if (step.Outcome == ReaderOutcome.BrowserError)
            return 1;
    }
    var snapshot = await browser.CaptureAsync(cancellation.Token);
    var reached = snapshot.Nodes.Any(node => node.Role == "heading" && node.Name == "Details opened");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        FixtureGoalReached = reached,
        snapshot.Limitations,
        Assessment = "Synthetic fixture only; no claim about real screen-reader compatibility."
    }, json));
    return reached ? 0 : 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled. No further input was dispatched.");
    return 130;
}
catch (PlaywrightException)
{
    Console.Error.WriteLine("Chromium could not start. Build and run the documented Playwright install command.");
    return 1;
}
catch (BrowserOperationException exception)
{
    Console.Error.WriteLine($"Browser operation failed: {exception.Code}");
    return 1;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("LOCAL_ARTIFACT_WRITE_FAILED. Inspect retained files and .pending markers; original files were not replaced.");
    return 1;
}
