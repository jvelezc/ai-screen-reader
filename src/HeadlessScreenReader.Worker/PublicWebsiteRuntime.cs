using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public static class PublicWebsiteRuntime
{
    public static string Scripts => Path.Combine(AppContext.BaseDirectory, "public-reader-scripts");

    public static async Task<object> ProbeAsync(string root, string runtime, string image, CancellationToken token)
    {
        await using var supervisor = await IsolatedPublicReaderSupervisor.StartAsync(root, Scripts, runtime, image).ConfigureAwait(false);
        var organization = Guid.NewGuid(); var session = Guid.NewGuid();
        var source = PublicReaderSource.FromUrl("https://screenreader.privensic.com/ci/v1/source");
        var reader = await supervisor.OpenAsync(organization, session, source, token).ConfigureAwait(false);
        var next = await reader.ObserveAsync(token).ConfigureAwait(false);
        if (!reader.InitialSnapshot.Nodes.Any(node => node.Role == "RootWebArea" && node.Name == "Reader CI source v1")
            || next.DocumentId != reader.InitialSnapshot.DocumentId || next.DocumentUrl != source.TargetUrl)
            throw new InvalidDataException("PUBLIC_WEBSITE_RUNTIME_UNCONFIRMED");
        var retired = await supervisor.RetireAsync(organization, session).ConfigureAwait(false);
        if (retired is not { NativeShutdownConfirmed: true, ResourcesAbsenceConfirmed: true }) throw new InvalidDataException("PUBLIC_WEBSITE_RUNTIME_UNCONFIRMED");
        return new
        {
            Schema = "hsr-public-website-runtime-readiness-v1",
            State = "PublicWebsiteRuntimeReady",
            Image = image,
            Source = source.TargetUrl,
            Captures = 2,
            ObservedNodes = next.Nodes.Length,
            SameDocument = true,
            NativeShutdownConfirmed = true,
            ResourcesAbsenceConfirmed = true,
            supervisor.Recovered,
            DatabaseAccessed = false,
            ObservationPersisted = false,
            DispatchedInputs = 0,
            GoalVerified = false
        };
    }
}
