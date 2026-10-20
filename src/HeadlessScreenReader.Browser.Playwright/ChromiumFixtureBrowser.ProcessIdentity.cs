using System.Text.Json;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

/// <summary>Trusted fixture diagnostics, separate from reader-visible semantics. Contains no command line or URL.</summary>
public sealed record FixtureProcessIdentity(long BrowserProcessId, string BrowserContextId, string TargetId);

public sealed partial class ChromiumFixtureBrowser
{
    public async Task<FixtureProcessIdentity> CaptureFixtureProcessIdentityAsync(CancellationToken cancellationToken = default)
    {
        if (_publicPolicy is not null) throw new BrowserOperationException("PROCESS_IDENTITY_REQUIRES_TRUSTED_FIXTURE");
        ObjectDisposedException.ThrowIf(_disposed, this);
        var stage = "SESSION";
        try
        {
            return await _captureAdmission.RunAsync(async () =>
            {
                var session = await _browser.NewBrowserCDPSessionAsync().ConfigureAwait(false);
                try
                {
                    stage = "PROCESSES";
                    var processes = await session.SendAsync("SystemInfo.getProcessInfo").ConfigureAwait(false);
                    stage = "TARGET";
                    var target = await _cdp.SendAsync("Target.getTargetInfo").ConfigureAwait(false);
                    stage = "PARSE";
                    return ParseFixtureProcessIdentity(processes, target);
                }
                finally { await session.DetachAsync().ConfigureAwait(false); }
            }).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException or JsonException or InvalidOperationException)
        { throw new BrowserOperationException($"PROCESS_IDENTITY_{stage}_UNCONFIRMED"); }
    }

    internal static FixtureProcessIdentity ParseFixtureProcessIdentity(JsonElement? processes, JsonElement? target)
    {
        try
        {
            var items = processes!.Value.GetProperty("processInfo").EnumerateArray().ToArray();
            if (items.Length is < 1 or > 128) throw new InvalidOperationException();
            var browser = items.Single(i => i.GetProperty("type").GetString() == "browser");
            var pid = browser.GetProperty("id").GetInt64();
            var context = target!.Value.GetProperty("targetInfo").GetProperty("browserContextId").GetString();
            var id = target.Value.GetProperty("targetInfo").GetProperty("targetId").GetString();
            if (pid <= 0 || string.IsNullOrWhiteSpace(context) || context.Length > 256 || string.IsNullOrWhiteSpace(id)
                || id.Length > 256)
                throw new InvalidOperationException();
            return new(pid, context, id);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException or FormatException)
        { throw new BrowserOperationException("PROCESS_IDENTITY_UNCONFIRMED"); }
    }
}
