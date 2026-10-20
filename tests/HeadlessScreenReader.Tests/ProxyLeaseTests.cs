using HeadlessScreenReader.Egress;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class ProxyLeaseTests
{
    [Fact]
    public void StartupGrace_ExpiresAtSixtySecondsAndCannotBeRevived()
    {
        var time = new FakeTimeProvider();
        var lease = new ProxyLease("owner", time);
        time.Advance(TimeSpan.FromSeconds(60) - TimeSpan.FromTicks(1));
        Assert.False(lease.Expired);
        time.Advance(TimeSpan.FromTicks(1));
        Assert.True(lease.Expired);
        Assert.False(lease.Touch("owner"));
        Assert.True(lease.Expired);
    }

    [Fact]
    public void Pulse_UsesFifteenSecondDeadlineAndWrongOwnerCannotExtendIt()
    {
        var time = new FakeTimeProvider();
        var lease = new ProxyLease("owner", time);
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.True(lease.Touch("owner"));
        time.Advance(TimeSpan.FromSeconds(14));
        Assert.False(lease.Touch("other"));
        Assert.False(lease.Expired);
        Assert.True(lease.Touch("owner"));
        time.Advance(TimeSpan.FromSeconds(15) - TimeSpan.FromTicks(1));
        Assert.False(lease.Expired);
        time.Advance(TimeSpan.FromTicks(1));
        Assert.True(lease.Expired);
        Assert.False(lease.Touch("owner"));
    }

}
