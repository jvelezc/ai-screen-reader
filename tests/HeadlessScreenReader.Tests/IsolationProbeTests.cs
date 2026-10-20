using HeadlessScreenReader.Egress;

namespace HeadlessScreenReader.Tests;

public sealed class IsolationProbeTests
{
    [Theory]
    [InlineData("0000000000000000", true)]
    [InlineData("0000000000040000", true)]
    [InlineData("0000000000001000", false)] // NET_ADMIN belongs only to the separate fence.
    [InlineData("0000000000240000", false)] // SYS_ADMIN must never enter the worker.
    [InlineData("not-hex", false)]
    [InlineData("", false)]
    public void CapabilityGate_AllowsNoPrivilegesOrOnlyChrootAndRejectsBroaderSets(string value, bool allowed) =>
        Assert.Equal(allowed, IsolationProbe.CapabilitiesAreRestricted(value));
}
