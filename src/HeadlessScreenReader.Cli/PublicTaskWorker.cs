using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Egress;
using HeadlessScreenReader.ScreenReader;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

/// <summary>Input-authorized public task collector. Raw page data never enters its framed control lane.</summary>
internal static class PublicTaskWorker
{
    internal static async Task<int> RunAsync(string[] args, Stream input, Stream output, Stream control, CancellationToken token = default)
    {
        if (!OperatingSystem.IsLinux() || args.Length != 4 || !IPAddress.TryParse(args[0], out var proxy)
            || proxy.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(proxy)
            || !Guid.TryParseExact(args[2], "N", out var owner) || owner == Guid.Empty) return 2;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var heartbeatStop = new CancellationTokenSource();
        Task? heartbeat = null; string? folder = null; var leased = false;
        PublicTaskStart? started = null; var stage = "Start"; var activeAttempt = 0;
        PublicNavigationBrowser? activeBrowser = null;
        ImmutableArray<PublicTaskFailure>.Builder? activeFailures = null;
        ImmutableArray<PublicTaskEvent>.Builder? activeEvents = null;
        long activeBegan = 0; var omittedFailures = 0;
        try
        {
            var bytes = await ReaderWorkerWire.ReadAsync(input, 16 * 1024, stop.Token).ConfigureAwait(false) ?? throw new IOException();
            var start = JsonSerializer.Deserialize<PublicTaskStart>(bytes, ReaderWorkerWire.Json) ?? throw new IOException(); start.Validate();
            started = start; stage = "Isolation";
            if (start.ExecutionId != owner || start.Job.Source.TargetUrl != args[3] || start.Job.Source.AllowedHost != args[1]) throw new IOException();
            var policy = PublicTargetPolicy.ForPublicDependencies([args[1]]);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                if (!(await IsolationProbe.VerifyAsync(proxy, policy, deadline.Token).ConfigureAwait(false)).Passed) throw new IOException();
            }
            await PulseAsync(false, stop.Token).ConfigureAwait(false); leased = true;
            heartbeat = KeepAliveAsync();
            using var probeTraceDeadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); probeTraceDeadline.CancelAfter(TimeSpan.FromSeconds(3));
            var probeTrace = await ProxyProtocol.ReadTraceAsync(proxy, owner.ToString("N"), probeTraceDeadline.Token).ConfigureAwait(false);
            if (probeTrace.Policy != PublicTaskNetworkTrace.PublicDependencyPolicy)
                throw new BrowserOperationException("PUBLIC_TASK_EGRESS_PROFILE_MISMATCH");
            var probeThroughSequence = probeTrace.Events.LastOrDefault()?.Sequence ?? 0;
            folder = Path.Combine(Path.GetTempPath(), "hsr-public-task-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var sequence = 0; var frameSequence = 0; PublicTaskGoal? goal = null;
            PublicTaskNetworkTrace? discoveryNetwork = null;
            var files = ImmutableDictionary.CreateBuilder<string, byte[]>(); var inventory = ImmutableArray.CreateBuilder<PublicTaskFile>();
            var attempts = ImmutableArray.CreateBuilder<PublicTaskAttempt>();
            string browserVersion = "unavailable", playwrightVersion = "unavailable";
            for (var number = 1; number <= 3; number++)
            {
                activeAttempt = number; stage = "Open";
                activeBrowser = null;
                activeFailures = null; activeEvents = null; activeBegan = 0; omittedFailures = 0;
                await Authorize(number, "Open").ConfigureAwait(false);
                var events = ImmutableArray.CreateBuilder<PublicTaskEvent>();
                var now = DateTimeOffset.UtcNow; var began = System.Diagnostics.Stopwatch.GetTimestamp();
                var failures = ImmutableArray.CreateBuilder<PublicTaskFailure>();
                activeFailures = failures; activeEvents = events; activeBegan = began;
                var beforePath = $"attempt-{number}-before.json"; var afterPath = $"attempt-{number}-after.json"; var videoPath = $"attempt-{number}.webm";
                var method = number < 3 ? "Keyboard" : "Pointer";
                var outcome = "Unconfirmed"; string? error = null, finalUrl = null; var same = false; var denials = 0;
                PublicTaskMedia? media = null; PublicNavigationBrowser? browser = null; var closeDeclared = false;
                PublicTaskReaderController? reader = null; PublicTaskReaderRecord? initialReader = null;
                PublicTaskKeyboardPath? path = null;
                AccessibilitySnapshot? beforeSnapshot = null;
                using var liveStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                Task? liveFrames = null;
                string? liveKey = null, liveFocus = null;
                try
                {
                    browser = await PublicNavigationBrowser.OpenAsync(start.Job, proxy, Path.Combine(folder, $"attempt-{number}"), stop.Token).ConfigureAwait(false);
                    activeBrowser = browser;
                    liveFrames = PublishFramesAsync();
                    stage = "Discover";
                    browserVersion = browser.BrowserVersion; playwrightVersion = browser.PlaywrightVersion;
                    goal ??= await browser.DiscoverAsync(stop.Token).ConfigureAwait(false);
                    if (goal is null)
                    {
                        await Authorize(number, "Close").ConfigureAwait(false);
                        closeDeclared = true;
                        await browser.FinishAsync(stop.Token).ConfigureAwait(false);
                        discoveryNetwork = browser.Network;
                        break;
                    }
                    if (number < 3) path = new(goal);
                    same = await browser.SameGoalAsync(goal, stop.Token).ConfigureAwait(false);
                    reader = PublicTaskReaderController.Create(browser, goal, (key, _) => Authorize(number, key), TimeProvider.System, began);
                    stage = "Snapshot";
                    initialReader = await reader.ExecuteAsync("Observe", stop.Token).ConfigureAwait(false);
                    if (initialReader.Outcome != "Observed") throw new BrowserOperationException("PUBLIC_TASK_READER_UNCONFIRMED");
                    RememberFocus();
                    beforeSnapshot = reader.Snapshot ?? throw new BrowserOperationException("PUBLIC_TASK_READER_UNCONFIRMED");
                    Add(beforePath, JsonSerializer.SerializeToUtf8Bytes(beforeSnapshot, ReaderWorkerWire.Json), "application/json");
                    if (same)
                    {
                        stage = "Input";
                        if (number == 3) await Input("Pointer").ConfigureAwait(false);
                        else
                        {
                            while (path!.Continue(reader.Snapshot?.DocumentId, reader.Focus, events.Count))
                                await Input("Tab").ConfigureAwait(false);
                            if (reader.Focus?.AnchorUrl == goal.DestinationUrl)
                                await Input("Enter").ConfigureAwait(false);
                        }
                    }
                    finalUrl = start.Job.Source.AllowsDocument(browser.CurrentUrl) ? browser.CurrentUrl : null;
                    var reached = await browser.GoalObservedAsync(goal, stop.Token).ConfigureAwait(false);
                    if (number == 3 && browser.HasRecentPointerFrame)
                        await Task.Delay(1100, stop.Token).ConfigureAwait(false);
                    denials = Math.Min(1000, browser.PolicyDenials);
                    outcome = reached ? "Reached" : same && finalUrl == start.Job.Source.TargetUrl ? "NotReached" : "Unconfirmed";
                    if (denials != 0) { error = "PUBLIC_TASK_POLICY_INTERRUPTED"; outcome = "Unconfirmed"; }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception problem)
                {
                    path?.Unconfirmed(events.Count(e => e.Action == "Tab"));
                    await Diagnostic(problem).ConfigureAwait(false);
                    if (goal is null) throw;
                    outcome = "Unconfirmed"; error = "PUBLIC_TASK_ATTEMPT_UNCONFIRMED";
                }
                finally
                {
                    await liveStop.CancelAsync().ConfigureAwait(false);
                    if (liveFrames is not null) await liveFrames.ConfigureAwait(false);
                    // A fast keyboard route can finish before the next scheduled frame.
                    if (number < 3 && liveKey is not null)
                        await PublishFrameAsync(reuseLatest: true).ConfigureAwait(false);
                    // A failed input is precisely when the original recording matters most.
                    // Retain permitted observations/media even when the task outcome is uncertain.
                    if (browser is not null && goal is not null)
                    {
                        finalUrl = start.Job.Source.AllowsDocument(browser.CurrentUrl) ? browser.CurrentUrl : null;
                        try
                        {
                            stage = "Snapshot";
                            var afterSnapshot = await browser.CaptureAsync(stop.Token).ConfigureAwait(false);
                            Add(afterPath, JsonSerializer.SerializeToUtf8Bytes(afterSnapshot, ReaderWorkerWire.Json), "application/json");
                            if (outcome == "NotReached" && (beforeSnapshot is null || !PublicTaskRules.SameExposedContent(beforeSnapshot, afterSnapshot)))
                            { outcome = "Unconfirmed"; error = "PUBLIC_TASK_ATTEMPT_UNCONFIRMED"; }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception problem) { await Diagnostic(problem).ConfigureAwait(false); outcome = "Unconfirmed"; error = "PUBLIC_TASK_ATTEMPT_UNCONFIRMED"; }
                        if (!closeDeclared) { await Authorize(number, "Close").ConfigureAwait(false); closeDeclared = true; }
                        stage = "Recording";
                        try
                        {
                            media = await browser.FinishAsync(stop.Token).ConfigureAwait(false);
                            if (!media.CaptureStopped || !media.BrowserClosed || media.DecodedFrames < 1) throw new IOException();
                            Add(videoPath, media.Video, "video/webm");
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception problem) { await Diagnostic(problem).ConfigureAwait(false); outcome = "Unconfirmed"; error = "PUBLIC_TASK_ATTEMPT_UNCONFIRMED"; }
                    }
                    try
                    {
                        if (browser is not null) await browser.DisposeAsync().ConfigureAwait(false);
                        if (!closeDeclared) await Authorize(number, "Close").ConfigureAwait(false);
                    }
                    finally { reader?.Dispose(); }
                }
                denials = Math.Min(1000, browser?.PolicyDenials ?? denials);
                if (denials != 0) { outcome = "Unconfirmed"; error = "PUBLIC_TASK_POLICY_INTERRUPTED"; }
                attempts.Add(new(number, Guid.NewGuid(), method, now, outcome, error, finalUrl, same, denials, events.ToImmutable(),
                    beforePath, afterPath, videoPath, media?.BrowserClosed == true, media?.CaptureStopped == true, media?.DecodedFrames ?? 0)
                {
                    InitialReader = initialReader,
                    Network = browser?.Network,
                    Failures = failures.ToImmutable(),
                    OmittedFailures = omittedFailures,
                    Navigation = path?.Record()
                });

                async Task Input(string action)
                {
                    var before = reader!.Focus;
                    var url = browser.CurrentUrl;
                    var attempted = false; var completed = false; PublicTaskReaderRecord? record = null;
                    try
                    {
                        if (action == "Pointer")
                        {
                            await Authorize(number, action).ConfigureAwait(false);
                            attempted = true;
                            await browser.PointerAsync(goal!, stop.Token).ConfigureAwait(false); completed = true;
                            record = await reader.ExecuteAsync("Observe", stop.Token).ConfigureAwait(false);
                        }
                        else
                        {
                            record = await reader.ExecuteAsync(action, stop.Token).ConfigureAwait(false);
                            attempted = reader.InputAttempted; completed = reader.InputCompleted;
                            before = reader.BeforeFocus;
                        }
                        if (record.Outcome != "Observed" || action != "Pointer" && !completed)
                            throw new BrowserOperationException("PUBLIC_TASK_READER_UNCONFIRMED");
                    }
                    catch (BrowserInputDeniedException) { attempted = false; throw; }
                    finally
                    {
                        if (action != "Pointer") { attempted = reader.InputAttempted; completed = reader.InputCompleted; before = reader.BeforeFocus; }
                        var after = record?.Outcome == "Observed" ? reader.Focus : null;
                        events.Add(new(events.Count + 1, System.Diagnostics.Stopwatch.GetElapsedTime(began).TotalMilliseconds,
                            action, before, after, url, attempted, completed)
                        { Reader = record });
                    }
                    if (action != "Pointer" && completed)
                    {
                        Volatile.Write(ref liveKey, action);
                        RememberFocus();
                    }
                }

                void RememberFocus()
                {
                    var name = number < 3 ? reader?.Focus?.Name : null;
                    Volatile.Write(ref liveFocus, name is not null && PublicTaskRules.Text(name, 160) ? name : null);
                }

                async Task PublishFramesAsync()
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(650));
                    try
                    {
                        do
                        {
                            await PublishFrameAsync().ConfigureAwait(false);
                        } while (await timer.WaitForNextTickAsync(liveStop.Token).ConfigureAwait(false));
                    }
                    catch (OperationCanceledException) when (liveStop.IsCancellationRequested) { }
                }

                async Task PublishFrameAsync(bool reuseLatest = false)
                {
                    var captured = browser?.TakeLiveFrame(reuseLatest);
                    if (captured is null || DateTimeOffset.UtcNow - captured.CapturedAt >= PublicTaskLiveFrame.MaximumAge ||
                        !PublicTaskLiveFrame.TryJpegSize(captured.Data, out var width, out var height)) return;
                    var frame = new PublicTaskLiveFrame(PublicTaskLiveFrame.CurrentSchema, start.Job.OrganizationId, start.Job.ParentScanId,
                        start.Job.JobId, start.ExecutionId, number, ++frameSequence, captured.CapturedAt, width, height, captured.Data,
                        PublicTaskRules.Hash(captured.Data))
                    {
                        Pointer = number == 3 ? captured.Pointer : null,
                        LastKey = Volatile.Read(ref liveKey),
                        FocusName = Volatile.Read(ref liveFocus)
                    };
                    try
                    {
                        // Complete each message before opening the evidence archive on this stream.
                        await PublicTaskLiveWire.WriteFrameAsync(output, frame, stop.Token).ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is IOException or OperationCanceledException)
                    { /* Unavailable live transport cannot supply a task outcome. */ }
                }
            }
            stage = "Report";
            using var traceDeadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); traceDeadline.CancelAfter(TimeSpan.FromSeconds(3));
            var egressTrace = await ProxyProtocol.ReadTraceAsync(proxy, owner.ToString("N"), traceDeadline.Token).ConfigureAwait(false);
            var report = new PublicTaskReport(PublicTaskReport.CurrentSchema, start, goal, attempts.ToImmutable(), inventory.ToImmutable(),
                browserVersion, playwrightVersion, System.Runtime.InteropServices.RuntimeInformation.OSDescription, "1280x720",
                PublicTaskRules.Hash(await File.ReadAllBytesAsync(typeof(PublicTaskWorker).Assembly.Location, stop.Token).ConfigureAwait(false)))
            {
                ReaderProfile = ScreenReaderSession.Profile,
                AssessmentPolicy = PublicTaskReport.TransportAttributionPolicy,
                NetworkPolicy = PublicTaskNetworkTrace.PublicDependencyPolicy,
                Egress = egressTrace,
                EgressIsolationProbeThroughSequence = probeThroughSequence,
                DiscoveryNetwork = discoveryNetwork
            };
            report.Validate(start);
            files.Add("report.json", JsonSerializer.SerializeToUtf8Bytes(report, ReaderWorkerWire.Json));
            var bundle = new PublicTaskBundle(report, files.ToImmutable()).Encode();
            await Authorize(0, "Publish").ConfigureAwait(false);
            await PublicTaskLiveWire.WriteArchiveAsync(output, bundle, stop.Token).ConfigureAwait(false);
            return 0;

            void Add(string path, byte[] content, string type)
            { files.Add(path, content); inventory.Add(new(path, PublicTaskRules.Hash(content), content.Length, type)); }
            async Task Authorize(int attempt, string action)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
                var intent = new PublicTaskIntent(start, ++sequence, attempt, action, goal);
                var payload = JsonSerializer.SerializeToUtf8Bytes(intent, ReaderWorkerWire.Json);
                await ReaderWorkerWire.WriteAsync(control, payload, 16 * 1024, deadline.Token).ConfigureAwait(false);
                var ackBytes = await ReaderWorkerWire.ReadAsync(input, 4096, deadline.Token).ConfigureAwait(false) ?? throw new IOException();
                var ack = JsonSerializer.Deserialize<PublicTaskAck>(ackBytes, ReaderWorkerWire.Json);
                if (ack is null || ack.ExecutionId != start.ExecutionId || ack.Sequence != sequence || ack.IntentSha256 != PublicTaskRules.Hash(payload))
                    throw new IOException();
            }
        }
        catch (Exception problem) { await Diagnostic(problem).ConfigureAwait(false); return 3; }
        finally
        {
            heartbeatStop.Cancel(); if (heartbeat is not null) { try { await heartbeat.ConfigureAwait(false); } catch { } }
            if (leased) { try { await PulseAsync(true, CancellationToken.None).ConfigureAwait(false); } catch { } }
            if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        async Task KeepAliveAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            try { while (await timer.WaitForNextTickAsync(heartbeatStop.Token).ConfigureAwait(false)) await PulseAsync(false, stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested) { }
            catch { await stop.CancelAsync().ConfigureAwait(false); }
        }
        async Task Diagnostic(Exception problem)
        {
            if (started is null) return;
            try
            {
                var failure = PublicTaskFailureCapture.Create(problem, stage, DateTimeOffset.UtcNow,
                    activeBegan == 0 ? 0 : System.Diagnostics.Stopwatch.GetElapsedTime(activeBegan).TotalMilliseconds, activeEvents?.Count ?? 0);
                if (activeFailures is not null)
                {
                    if (activeFailures.Count < 4) activeFailures.Add(failure);
                    else omittedFailures = Math.Min(1000, omittedFailures + 1);
                }
                PublicTaskNetworkTrace? egress = null;
                if (leased)
                {
                    using var traceStop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try { egress = await ProxyProtocol.ReadTraceAsync(proxy, owner.ToString("N"), traceStop.Token).ConfigureAwait(false); }
                    catch { /* The original failure remains primary; absent diagnostics are never fabricated. */ }
                }
                var diagnostic = new PublicTaskDiagnostic(started, activeAttempt, stage, failure.Category, failure.Code)
                { Network = problem.Data["PublicTaskNetworkTrace"] as PublicTaskNetworkTrace ?? activeBrowser?.Network, Egress = egress, Failure = failure };
                await ReaderWorkerWire.WriteAsync(control, JsonSerializer.SerializeToUtf8Bytes(diagnostic, ReaderWorkerWire.Json),
                    PublicTaskDiagnostic.MaximumBytes, CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
        }
        async Task PulseAsync(bool close, CancellationToken cancellation)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(2));
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(proxy, 3128, deadline.Token).ConfigureAwait(false);
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"{(close ? "HSR-CLOSE" : "HSR-LEASE")} {owner:N} HTTP/1.1\r\n\r\n"), deadline.Token).ConfigureAwait(false);
            if (!(await ProxyProtocol.ReadHeaderAsync(client.GetStream(), deadline.Token).ConfigureAwait(false)).StartsWith("HTTP/1.1 200 ", StringComparison.Ordinal)) throw new IOException();
        }
    }
}
