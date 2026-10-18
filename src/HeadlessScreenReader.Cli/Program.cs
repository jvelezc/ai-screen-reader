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
    Console.WriteLine("HeadlessScreenReader. Usage: [--widgets | --observe-live | --text | --frames] [--headed] | --reference-template | --reference-case <id> | --assess-reference <file>");
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
if (referenceCase is null && (args.Any(arg => arg is not "--headed" and not "--widgets" and not "--observe-live" and not "--text" and not "--frames")
    || args.Count(arg => arg is "--widgets" or "--observe-live" or "--text" or "--frames") > 1))
{
    Console.Error.WriteLine("Invalid arguments or unknown reference case. Use --help; only bundled fixtures are supported.");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var json = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
try
{
    await using var browser = await ChromiumFixtureBrowser.LaunchAsync(
        headed: referenceCase is not null || args.Contains("--headed", StringComparer.Ordinal),
        cancellationToken: cancellation.Token,
        fixture: referenceCase?.Fixture
            ?? (args.Contains("--frames", StringComparer.Ordinal) ? BundledFixture.Frames
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
