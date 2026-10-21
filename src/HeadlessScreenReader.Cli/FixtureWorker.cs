using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli;

public static class FixtureWorker
{
    public static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<FixtureObservation> ObserveAsync(FixtureJob job, CancellationToken token = default)
    {
        job.Validate();
        if (BundledFixtureCatalog.Describe(BundledFixture.Traversal).ContentSha256 != FixtureObservation.ContentSha256)
            throw new BrowserOperationException("WORKER_FIXTURE_IDENTITY_CHANGED");
        var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal, cancellationToken: token);
        AccessibilitySnapshot snapshot;
        try { snapshot = await browser.CaptureAsync(token); }
        finally { await browser.DisposeAsync(); }
        var receipt = new FixtureObservation(FixtureObservation.CurrentSchema, job, "synthetic-shop",
            FixtureObservation.Profile, FixtureObservation.ContentSha256, snapshot, 0, false, browser.ShutdownStatus);
        receipt.Validate(job);
        return receipt;
    }

    public static async Task<int> RunAsync(Stream input, Stream output)
    {
        try
        {
            using var bytes = new MemoryStream();
            var buffer = new byte[1024];
            while (true)
            {
                var read = await input.ReadAsync(buffer);
                if (read == 0) break;
                if (bytes.Length + read > 1024) throw new BrowserOperationException("WORKER_DESCRIPTOR_INVALID");
                bytes.Write(buffer, 0, read);
            }
            var job = JsonSerializer.Deserialize<FixtureJob>(bytes.ToArray(), Json)
                ?? throw new BrowserOperationException("WORKER_DESCRIPTOR_INVALID");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var observation = await ObserveAsync(job, timeout.Token);
            var result = JsonSerializer.SerializeToUtf8Bytes(observation, Json);
            if (result.Length > FixtureObservation.MaximumBytes) throw new BrowserOperationException("WORKER_OBSERVATION_TOO_LARGE");
            await output.WriteAsync(result);
            return 0;
        }
        catch (Exception)
        {
            // Engine/runtime messages can contain private data. Never forward them to the coordinator log.
            Console.Error.WriteLine("FIXTURE_OBSERVATION_UNCONFIRMED");
            return 3;
        }
    }
}
