using HeadlessScreenReader.Browser.Playwright;

namespace HeadlessScreenReader.Cli;

/// <summary>Linux process facts only; the original argument payload is never retained.</summary>
public sealed record HeadedProcessObservation(FixtureProcessIdentity Identity, bool HeadlessSwitchObserved, bool NoSandboxSwitchObserved)
{
    public static HeadedProcessObservation Read(FixtureProcessIdentity identity)
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("HEADED_PROOF_REQUIRES_LINUX");
        return Parse(identity, File.ReadAllText($"/proc/{identity.BrowserProcessId}/cmdline"));
    }
    public static HeadedProcessObservation Parse(FixtureProcessIdentity identity, string arguments)
    {
        if (identity.BrowserProcessId <= 0 || arguments.Length is < 1 or > 65536)
            throw new InvalidOperationException("HEADED_PROCESS_ARGUMENTS_UNCONFIRMED");
        var args = arguments.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length is < 2 or > 512) throw new InvalidOperationException("HEADED_PROCESS_ARGUMENTS_UNCONFIRMED");
        return new(identity, args.Any(a => a == "--headless" || a.StartsWith("--headless=", StringComparison.Ordinal)),
            args.Any(a => a == "--no-sandbox" || a.StartsWith("--no-sandbox=", StringComparison.Ordinal)));
    }
}
