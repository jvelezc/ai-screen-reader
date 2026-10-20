using System.Globalization;
using System.Text.RegularExpressions;

namespace HeadlessScreenReader.Cli;

public sealed record HeadedDisplayObservation(int Number, int ProcessId, bool PrivateAuthority, bool TcpDisabled)
{
    public static int ParseNumber(string? display)
    {
        if (display is null || display.Length > 8) throw new InvalidOperationException("HEADED_DISPLAY_INVALID");
        var match = Regex.Match(display, @"\A:([0-9]{1,4})(?:\.0)?\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) throw new InvalidOperationException("HEADED_DISPLAY_INVALID");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    public static HeadedDisplayObservation ReadCurrent()
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("HEADED_PROOF_REQUIRES_LINUX");
        var number = ParseNumber(Environment.GetEnvironmentVariable("DISPLAY"));
        var authority = Environment.GetEnvironmentVariable("XAUTHORITY");
        if (string.IsNullOrEmpty(authority) || !Path.GetFullPath(authority).StartsWith("/tmp/xvfb-run.", StringComparison.Ordinal))
            throw new InvalidOperationException("HEADED_AUTHORITY_UNCONFIRMED");
        var mode = File.GetUnixFileMode(authority);
        var directoryMode = File.GetUnixFileMode(Path.GetDirectoryName(authority)!);
        const UnixFileMode other = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((mode & other) != 0 || (directoryMode & other) != 0 || new FileInfo(authority).Length is < 1 or > 4096)
            throw new InvalidOperationException("HEADED_AUTHORITY_UNCONFIRMED");
        var pid = int.Parse(File.ReadAllText($"/tmp/.X{number}-lock").Trim(), CultureInfo.InvariantCulture);
        if (pid <= 0 || File.ReadAllText($"/proc/{pid}/comm").Trim() != "Xvfb")
            throw new InvalidOperationException("HEADED_PROCESS_UNCONFIRMED");
        var args = File.ReadAllText($"/proc/{pid}/cmdline").Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var tcpDisabled = args.Zip(args.Skip(1)).Any(pair => pair.First == "-nolisten" && pair.Second == "tcp");
        if (!args.Contains($":{number}", StringComparer.Ordinal) || !tcpDisabled)
            throw new InvalidOperationException("HEADED_PROCESS_UNCONFIRMED");
        return new(number, pid, true, tcpDisabled);
    }
}
