namespace HeadlessScreenReader.Tests;

internal static class NativeShutdownTestSync
{
    public static async Task UntilAsync(Func<bool> condition)
    {
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(1, bound.Token);
    }
}
