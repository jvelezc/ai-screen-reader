using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumPortalTests
{
    [Theory]
    [InlineData(320, "LocalFallback", "Server disk; object copy not found")]
    [InlineData(736, "future-storage", "Storage location unconfirmed")]
    [InlineData(736, "constructor", "Storage location unconfirmed")]
    [InlineData(1120, "S3", "Verified private object storage")]
    public async Task EvidenceCopyLabelDistinguishesRemoteLegacyAndUnknownWithoutChangingGoalClaims(int width, string storage, string label)
    {
        var project = new PortalProjectStub(); project.Record = project.Record with { State = "observed" };
        project.Job = project.Job with { State = "completed", Generation = 1 };
        var reader = new PortalObservationStub
        {
            Value = new("document", new string('a', 64), "hsr-desktop-reader-v13", 0, false,
            [new("node", null, "heading", "Synthetic shop", false, false, 1, null, null, null, null, null, null)], [], storage)
        };
        await using var server = await PortalHttpTests.Start(project, reader);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = width, Height = 900 } });
        page.SetDefaultTimeout(5000); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
        await page.RouteAsync(PortalOptions.Project.GetLeftPart(UriPartial.Authority) + "/auth/v1/**", route => route.FulfillAsync(new()
        { ContentType = "application/json", Body = JsonSerializer.Serialize(new { access_token = PortalProjectClientTests.Bearer }) }));
        await page.GotoAsync(server.BaseAddress.ToString());
        await page.Locator("#email").FillAsync("reader@example.invalid"); await page.Locator("#password").FillAsync("synthetic-login-only");
        await page.Locator("#login").ClickAsync(); await Assertions.Expect(page.Locator("#workspace")).ToBeVisibleAsync();
        await page.Locator("#run").ClickAsync(); await Assertions.Expect(page.Locator("#facts")).ToContainTextAsync(label);
        var facts = await page.Locator("#facts").InnerTextAsync();
        Assert.Contains("Evidence copy", facts); Assert.Contains("Not tested", facts); Assert.Contains("0 keys", facts);
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"), await LayoutFacts(page));
        Assert.Empty(errors);
        var folder = Path.Combine(RepositoryRoot(), "artifacts", "s3-validation", "portal"); Directory.CreateDirectory(folder);
        await page.ScreenshotAsync(new() { Path = Path.Combine(folder, "storage-" + width + ".png"), FullPage = true });
        await page.Locator("#logout").ClickAsync(); Assert.Empty(await page.Locator("#facts").InnerTextAsync());
    }

    [Theory]
    [InlineData(1120)]
    [InlineData(320)]
    public async Task NativeKeyboardSignInRunVerifiedResultAndSignOutWorkWithoutCredentialPersistence(int width)
    {
        var root = Path.Combine(RepositoryRoot(), "artifacts", "portal-validation", "browser-" + width);
        Directory.CreateDirectory(root);
        var job = new FixtureJob(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, Guid.NewGuid(), 1);
        var observed = await FixtureWorker.ObserveAsync(job);
        var stored = await new ObservationStore(root).PersistAsync(JsonSerializer.SerializeToUtf8Bytes(observed, ObservationStore.Json), job, default);
        var project = new PortalProjectStub();
        project.Record = project.Record with { State = "observed" };
        project.Job = new(job.OrganizationId, job.ScanId, job.JobId, "completed", 1, stored.Sha256, stored.Reference, null);
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, root, new string('a', 40), 0),
            project, new PortalObservationReader(root));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = width, Height = 900 } });
        page.SetDefaultTimeout(5000);
        var errors = new List<string>(); var urls = new List<string>(); var authCalls = 0;
        page.PageError += (_, error) => errors.Add(error);
        page.Request += (_, request) => urls.Add(request.Url);
        await page.RouteAsync(PortalOptions.Project.GetLeftPart(UriPartial.Authority) + "/auth/v1/**", async route =>
        {
            authCalls++;
            Assert.EndsWith("/token?grant_type=password", route.Request.Url);
            using var body = JsonDocument.Parse(route.Request.PostData!);
            Assert.Equal("reader@example.invalid", body.RootElement.GetProperty("email").GetString());
            Assert.Equal("synthetic-login-only", body.RootElement.GetProperty("password").GetString());
            await route.FulfillAsync(new() { ContentType = "application/json", Body = JsonSerializer.Serialize(new { access_token = PortalProjectClientTests.Bearer }) });
        });
        await page.GotoAsync(server.BaseAddress.ToString());
        await Assertions.Expect(page.Locator("#login")).ToBeEnabledAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(root, "signin.png"), FullPage = true });
        // Reach and submit the actual native form using keyboard input.
        await page.Keyboard.PressAsync("Tab"); await page.Keyboard.PressAsync("Tab");
        Assert.Equal("email", await page.EvaluateAsync<string>("document.activeElement.id"));
        await page.Keyboard.TypeAsync("reader@example.invalid"); await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.TypeAsync("synthetic-login-only"); await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#workspace")).ToBeVisibleAsync();
        await page.Locator("#run").ClickAsync();
        await Assertions.Expect(page.Locator("#result-message")).ToContainTextAsync("did not traverse or verify an objective");
        await Assertions.Expect(page.Locator("#nodes")).ToContainTextAsync("Synthetic shop");
        Assert.Contains("Not tested", await page.Locator("#facts").InnerTextAsync());
        Assert.Contains("0 keys", await page.Locator("#facts").InnerTextAsync());
        Assert.Equal(observed.Snapshot.Nodes.Length, await page.Locator("#nodes > li").CountAsync());
        Assert.Contains("(unnamed)", await page.Locator("#nodes").InnerTextAsync());
        Assert.DoesNotContain("Unnamed control", await page.Locator("#nodes").InnerTextAsync());
        await page.ScreenshotAsync(new() { Path = Path.Combine(root, "result.png"), FullPage = true });
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"),
            await LayoutFacts(page));
        Assert.Equal("", await page.Locator("#password").InputValueAsync());
        Assert.Equal(1, authCalls); Assert.Equal(1, project.CreateCalls);
        var downloadTask = page.WaitForDownloadAsync(); await page.Locator("#download").ClickAsync();
        var download = await downloadTask; var path = await download.PathAsync();
        var report = await File.ReadAllTextAsync(path!);
        Assert.DoesNotContain(PortalProjectClientTests.Bearer, report); Assert.Contains(stored.Sha256, report);
        await page.Locator("#logout").ClickAsync();
        await Assertions.Expect(page.Locator("#workspace")).ToBeHiddenAsync();
        Assert.Empty(await page.Locator("#nodes").InnerTextAsync());
        Assert.Empty(await page.Locator("#runs").InnerTextAsync());
        Assert.Equal(0, await page.EvaluateAsync<int>("localStorage.length + sessionStorage.length"));
        Assert.All(urls, url => { Assert.DoesNotContain("synthetic-login-only", url); Assert.DoesNotContain(PortalProjectClientTests.Bearer, url); });
        Assert.Empty(await page.Context.CookiesAsync()); Assert.Empty(errors);
    }

    [Fact]
    public async Task MembershipHiddenByTenantRlsClearsPreviouslyDisplayedPrivateEvidence()
    {
        var project = new PortalProjectStub(); project.Record = project.Record with { State = "observed" };
        project.Job = project.Job with { State = "completed", Generation = 1 };
        var reader = new PortalObservationStub
        {
            Value = new("document", new string('a', 64), "hsr-desktop-reader-v13", 0, false,
            [new("node", null, "paragraph", "Previously authorized observation", false, false, null, null, null, null, null, null, null)], [])
        };
        await using var server = await PortalHttpTests.Start(project, reader);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        var page = await browser.NewPageAsync(); page.SetDefaultTimeout(5000);
        await page.RouteAsync(PortalOptions.Project.GetLeftPart(UriPartial.Authority) + "/auth/v1/**", route => route.FulfillAsync(new()
        { ContentType = "application/json", Body = JsonSerializer.Serialize(new { access_token = PortalProjectClientTests.Bearer }) }));
        await page.GotoAsync(server.BaseAddress.ToString());
        await page.Locator("#email").FillAsync("reader@example.invalid"); await page.Locator("#password").FillAsync("synthetic-login-only");
        await page.Locator("#login").ClickAsync(); await Assertions.Expect(page.Locator("#workspace")).ToBeVisibleAsync();
        await page.Locator("#run").ClickAsync(); await Assertions.Expect(page.Locator("#nodes")).ToContainTextAsync("Previously authorized observation");
        // Auth still verifies this user; current RLS visibility disappears independently.
        project.WorkspaceHidden = true;
        await page.Locator("#refresh").ClickAsync();
        await Assertions.Expect(page.Locator("#workspace")).ToBeHiddenAsync();
        Assert.Empty(await page.Locator("#nodes").InnerTextAsync()); Assert.Empty(await page.Locator("#runs").InnerTextAsync());
        await Assertions.Expect(page.Locator("#download")).ToBeDisabledAsync();
        Assert.False(project.Revoked);
        await Assertions.Expect(page.Locator("#error")).ToContainTextAsync("workspace is no longer available");
        var capture = Path.Combine(RepositoryRoot(), "artifacts", "portal-validation", "membership-revoked.png");
        Directory.CreateDirectory(Path.GetDirectoryName(capture)!);
        await page.ScreenshotAsync(new() { Path = capture, FullPage = true });
    }

    [Fact]
    public async Task NarrowViewportPreservesProductNameWhenTextIsEnlarged()
    {
        await using var server = await PortalHttpTests.Start(new());
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 320, Height = 900 } });
        // A user-selected larger base font stresses the same unbreakable product title
        // across platform font metrics, without relaxing CSP or hiding any text.
        await page.RouteAsync("**/portal.css", route => route.FulfillAsync(new()
        { ContentType = "text/css", Body = PortalAssets.Css + "\n:root{font-size:20px}" }));
        await page.GotoAsync(server.BaseAddress.ToString());
        await Assertions.Expect(page.Locator("#login")).ToBeEnabledAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"), await LayoutFacts(page));
        Assert.Equal("HeadlessScreenReader", await page.Locator("h1").InnerTextAsync());
        Assert.True(await page.Locator("h1").EvaluateAsync<bool>("e => e.getBoundingClientRect().height > parseFloat(getComputedStyle(e).lineHeight)"));
    }

    [Fact]
    public async Task PkceCallbackClearsCodeAndVerifierWhileObservedMarkupRemainsLiteralText()
    {
        var project = new PortalProjectStub(); project.Record = project.Record with { State = "observed" };
        project.Job = project.Job with { State = "completed", Generation = 1 };
        const string markup = "<img src=x onerror='window.hsrInjected=true'>";
        var reader = new PortalObservationStub
        {
            Value = new("document", new string('a', 64), "hsr-desktop-reader-v13", 0, false,
            [new("node", null, "paragraph", markup, false, false, null, null, null, null, null, null, null)], [])
        };
        await using var server = await PortalHttpTests.Start(project, reader);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        var page = await browser.NewPageAsync(); page.SetDefaultTimeout(5000);
        await page.AddInitScriptAsync("localStorage.setItem('hsr.pkce.gpnijaxrgtwzwtrqsbcu',JSON.stringify({verifier:'synthetic-pkce-verifier',expires:Date.now()+60000}));");
        var exchanges = 0;
        await page.RouteAsync(PortalOptions.Project.GetLeftPart(UriPartial.Authority) + "/auth/v1/**", async route =>
        {
            exchanges++;
            Assert.EndsWith("/token?grant_type=pkce", route.Request.Url);
            using var body = JsonDocument.Parse(route.Request.PostData!);
            Assert.Equal("synthetic-one-use-code", body.RootElement.GetProperty("auth_code").GetString());
            Assert.Equal("synthetic-pkce-verifier", body.RootElement.GetProperty("code_verifier").GetString());
            Assert.False(new Uri(page.Url).Query.Length > 0);
            await route.FulfillAsync(new() { ContentType = "application/json", Body = JsonSerializer.Serialize(new { access_token = PortalProjectClientTests.Bearer }) });
        });
        await page.GotoAsync(server.BaseAddress + "?code=synthetic-one-use-code");
        await Assertions.Expect(page.Locator("#workspace")).ToBeVisibleAsync();
        Assert.Equal(1, exchanges); Assert.Equal(0, await page.EvaluateAsync<int>("localStorage.length"));
        Assert.Equal("", new Uri(page.Url).Query);
        await page.Locator("#run").ClickAsync();
        await Assertions.Expect(page.Locator("#nodes")).ToContainTextAsync(markup);
        Assert.Equal(0, await page.Locator("#nodes img").CountAsync());
        Assert.False(await page.EvaluateAsync<bool>("window.hsrInjected === true"));
    }

    [Fact]
    public async Task SignupUsesPkceAndSingleFlightWhileAccessDenialClearsPrivateResults()
    {
        var project = new PortalProjectStub();
        await using var server = await PortalHttpTests.Start(project);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ChromiumSandbox = true });
        var page = await browser.NewPageAsync(); page.SetDefaultTimeout(5000);
        var signupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSignup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var signups = 0;
        await page.RouteAsync(PortalOptions.Project.GetLeftPart(UriPartial.Authority) + "/auth/v1/**", async route =>
        {
            if (route.Request.Url.Contains("/signup?", StringComparison.Ordinal))
            {
                signups++;
                using var body = JsonDocument.Parse(route.Request.PostData!);
                Assert.Equal("s256", body.RootElement.GetProperty("code_challenge_method").GetString());
                Assert.Matches("^[A-Za-z0-9_-]{43}$", body.RootElement.GetProperty("code_challenge").GetString()!);
                Assert.DoesNotContain("code_verifier", route.Request.PostData!);
                Assert.Contains("redirect_to=" + Uri.EscapeDataString(server.BaseAddress.GetLeftPart(UriPartial.Authority)), route.Request.Url);
                signupEntered.SetResult(); await releaseSignup.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await route.FulfillAsync(new() { ContentType = "application/json", Body = "{}" });
            }
            else await route.FulfillAsync(new() { ContentType = "application/json", Body = JsonSerializer.Serialize(new { access_token = PortalProjectClientTests.Bearer }) });
        });
        await page.GotoAsync(server.BaseAddress.ToString());
        await Assertions.Expect(page.Locator("#signup")).ToBeEnabledAsync();
        await page.Locator("#email").FillAsync("reader@example.invalid"); await page.Locator("#password").FillAsync("synthetic-login-only");
        await page.Locator("#signup").ClickAsync(); await signupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assertions.Expect(page.Locator("#login")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("#signup")).ToBeDisabledAsync();
        // Native Enter must not create a parallel password flow while signup is pending.
        await page.Locator("#password").PressAsync("Enter");
        Assert.Equal(0, project.AuthCalls);
        releaseSignup.SetResult();
        await Assertions.Expect(page.Locator("#state-title")).ToHaveTextAsync("Check your email");
        Assert.Equal(1, signups);
        Assert.Equal(1, await page.EvaluateAsync<int>("localStorage.length"));
        var local = await page.EvaluateAsync<string>("localStorage.getItem(localStorage.key(0))");
        Assert.DoesNotContain("synthetic-login-only", local); Assert.DoesNotContain("access_token", local);
        await page.Locator("#password").FillAsync("synthetic-login-only"); await page.Locator("#login").ClickAsync();
        await Assertions.Expect(page.Locator("#workspace")).ToBeVisibleAsync();
        project.Revoked = true; await page.Locator("#refresh").ClickAsync();
        await Assertions.Expect(page.Locator("#workspace")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#error")).ToContainTextAsync("sign in again", new() { IgnoreCase = true });
        Assert.Empty(await page.Locator("#runs").InnerTextAsync());
        Assert.Empty(await page.Locator("#nodes").InnerTextAsync());
    }
    private static Task<string> LayoutFacts(IPage page) => page.EvaluateAsync<string>("""
        () => JSON.stringify({viewport:innerWidth,document:document.documentElement.scrollWidth,
          elements:[...document.querySelectorAll('main *')].filter(e=>e.scrollWidth>e.clientWidth+1)
            .map(e=>({tag:e.tagName,id:e.id,scroll:e.scrollWidth,client:e.clientWidth,font:getComputedStyle(e).font}))})
        """);
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "HeadlessScreenReader.slnx"))) return directory.FullName;
        throw new InvalidOperationException("TEST_REPOSITORY_ROOT_UNCONFIRMED");
    }
}
