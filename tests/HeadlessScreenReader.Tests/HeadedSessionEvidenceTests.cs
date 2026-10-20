using System.Text.Json;
using System.Collections.Immutable;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class HeadedSessionEvidenceTests
{
    [Theory]
    [InlineData("0000000000000000")]
    [InlineData("0000000000040000")]
    public void ZeroOrOnlySandboxCapability_IsAcceptedInEveryActualMask(string mask)
    {
        var masks = new[] { "CapEff", "CapPrm", "CapInh", "CapBnd", "CapAmb" }.ToImmutableDictionary(field => field, _ => mask);
        Assert.True(new HeadedKernelObservation(1001, true, masks).Restricted);
    }

    [Theory]
    [InlineData("CapEff")]
    [InlineData("CapPrm")]
    [InlineData("CapInh")]
    [InlineData("CapBnd")]
    [InlineData("CapAmb")]
    public void AdditionalCapabilityInAnySet_IsRejected(string field)
    {
        var masks = new[] { "CapEff", "CapPrm", "CapInh", "CapBnd", "CapAmb" }.ToImmutableDictionary(name => name, _ => "0000000000000000");
        Assert.False(new HeadedKernelObservation(1001, true, masks.SetItem(field, "0000000000041000")).Restricted);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("privileges")]
    [InlineData("missing")]
    [InlineData("invalid")]
    public void IncompleteOrPrivilegedKernelEvidence_CannotPass(string caseId)
    {
        var masks = new[] { "CapEff", "CapPrm", "CapInh", "CapBnd", "CapAmb" }.ToImmutableDictionary(name => name, _ => "0");
        if (caseId == "missing") masks = masks.Remove("CapAmb");
        if (caseId == "invalid") masks = masks.SetItem("CapAmb", "invalid");
        Assert.False(new HeadedKernelObservation(caseId == "root" ? 0 : 1001, caseId != "privileges", masks).Restricted);
    }
    [Theory]
    [InlineData(":0", 0)]
    [InlineData(":99", 99)]
    [InlineData(":99.0", 99)]
    [InlineData(":9999", 9999)]
    public void LocalDisplayNumbers_ParseOnlySupportedUnixDisplays(string display, int expected) =>
        Assert.Equal(expected, HeadedDisplayObservation.ParseNumber(display));

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("remote")]
    [InlineData("unix-prefix")]
    [InlineData("negative")]
    [InlineData("large")]
    [InlineData("screen")]
    [InlineData("newline")]
    public void UnsupportedDisplayForms_AreRejectedBeforeProcessOrFileLookup(string caseId)
    {
        var value = caseId switch
        {
            "missing" => null,
            "empty" => "",
            "remote" => "localhost:99",
            "unix-prefix" => "unix:99",
            "negative" => ":-1",
            "large" => ":10000",
            "screen" => ":99.1",
            _ => ":99\n"
        };
        Assert.Equal("HEADED_DISPLAY_INVALID", Assert.Throws<InvalidOperationException>(() => HeadedDisplayObservation.ParseNumber(value)).Message);
    }

    [Theory]
    [InlineData("ordinary", false, false)]
    [InlineData("headless", true, false)]
    [InlineData("headless-value", true, false)]
    [InlineData("sandbox", false, true)]
    [InlineData("sandbox-value", false, true)]
    [InlineData("similar-prefix", false, false)]
    public void ActualArgumentFlags_AreExactAndOriginalPayloadIsNeverRetained(string caseId, bool headless, bool sandbox)
    {
        var flag = caseId switch
        {
            "headless" => "--headless",
            "headless-value" => "--headless=new",
            "sandbox" => "--no-sandbox",
            "sandbox-value" => "--no-sandbox=true",
            "similar-prefix" => "--headlessish",
            _ => "--ordinary"
        };
        var identity = new FixtureProcessIdentity(42, "context", "target");
        var result = HeadedProcessObservation.Parse(identity, $"chrome\0{flag}\0--private-argument-canary\0");
        Assert.Equal(identity, result.Identity);
        Assert.Equal(headless, result.HeadlessSwitchObserved); Assert.Equal(sandbox, result.NoSandboxSwitchObserved);
        Assert.DoesNotContain("private-argument-canary", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("single")]
    [InlineData("bytes")]
    [InlineData("count")]
    [InlineData("pid")]
    public void UnconfirmedNativeArgumentEvidence_CannotProduceAProof(string caseId)
    {
        var arguments = caseId switch
        {
            "empty" => "",
            "single" => "chrome",
            "bytes" => new string('a', 65537),
            "count" => string.Join('\0', Enumerable.Repeat("flag", 513)),
            _ => "chrome\0--ordinary\0"
        };
        Assert.Throws<InvalidOperationException>(() => HeadedProcessObservation.Parse(
            new(caseId == "pid" ? 0 : 42, "context", "target"), arguments));
    }

    [Fact]
    public void DiagnosticIdentity_RetainsOnlyActualBrowserProcessContextAndTarget()
    {
        var result = ChromiumFixtureBrowser.ParseFixtureProcessIdentity(
            JsonSerializer.SerializeToElement(new { processInfo = new[] { new { type = "browser", id = 42 }, new { type = "renderer", id = 43 } } }),
            JsonSerializer.SerializeToElement(new { targetInfo = new { browserContextId = "context", targetId = "target", url = "private-url-canary" } }));
        Assert.Equal(new FixtureProcessIdentity(42, "context", "target"), result);
        Assert.DoesNotContain("private-url-canary", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("many")]
    [InlineData("pid")]
    [InlineData("context")]
    [InlineData("target")]
    [InlineData("long-context")]
    [InlineData("long-target")]
    public void AmbiguousOrInvalidCdpIdentity_FailsClosed(string caseId)
    {
        JsonElement? processes = caseId == "missing" ? null : JsonSerializer.SerializeToElement(new
        {
            processInfo = Enumerable.Range(0, caseId switch { "empty" => 0, "duplicate" => 2, "many" => 129, _ => 1 })
                .Select(_ => new { type = "browser", id = caseId == "pid" ? 0 : 42 }).ToArray()
        });
        var target = JsonSerializer.SerializeToElement(new
        {
            targetInfo = new
            {
                browserContextId = caseId switch { "context" => "", "long-context" => new string('c', 257), _ => "context" },
                targetId = caseId switch { "target" => "", "long-target" => new string('t', 257), _ => "target" }
            }
        });
        Assert.Equal("PROCESS_IDENTITY_UNCONFIRMED", Assert.Throws<BrowserOperationException>(
            () => ChromiumFixtureBrowser.ParseFixtureProcessIdentity(processes, target)).Code);
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task RealChromiumIdentity_IsStableAcrossSameContextExclusionAndDeniedDuringGap()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal);
        var before = await browser.CaptureFixtureProcessIdentityAsync();
        Assert.True(before.BrowserProcessId > 0); Assert.NotEmpty(before.BrowserContextId); Assert.NotEmpty(before.TargetId);
        var lease = await browser.ExcludeCaptureAsync();
        Assert.Equal("CAPTURE_EXCLUDED", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => browser.CaptureFixtureProcessIdentityAsync())).Code);
        await browser.ResumeCaptureAsync(lease);
        Assert.Equal(before, await browser.CaptureFixtureProcessIdentityAsync());
    }
}
