using System.Collections.Immutable;
using System.Globalization;

namespace HeadlessScreenReader.Cli;

/// <summary>Observed process facts for this trusted fixture proof, not a complete hostile-site isolation probe.</summary>
public sealed record HeadedKernelObservation(int UserId, bool NoNewPrivileges, ImmutableDictionary<string, string> CapabilityMasks)
{
    private static readonly string[] Fields = ["CapEff", "CapPrm", "CapInh", "CapBnd", "CapAmb"];
    public bool Restricted => UserId > 0 && NoNewPrivileges && CapabilityMasks.Count == Fields.Length
        && Fields.All(maskName => CapabilityMasks.TryGetValue(maskName, out var value)
            && ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bits) && (bits & ~0x40000UL) == 0);

    public static HeadedKernelObservation ReadCurrent()
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("HEADED_PROOF_REQUIRES_LINUX");
        var status = File.ReadAllLines("/proc/self/status");
        string Value(string name) => status.Single(line => line.StartsWith(name + ":", StringComparison.Ordinal)).Split(':', 2)[1].Trim();
        return new(int.Parse(Value("Uid").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture),
            Value("NoNewPrivs") == "1", Fields.ToImmutableDictionary(field => field, Value));
    }
}
