using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli;

internal static class PublicTaskFailureCapture
{
    internal static PublicTaskFailure Create(Exception error, string stage, DateTimeOffset occurred,
        double elapsed, int eventSequence)
    {
        var category = error switch
        {
            BrowserOperationException => "Browser",
            Microsoft.Playwright.PlaywrightException => "Playwright",
            JsonException => "Protocol",
            TimeoutException => "Timeout",
            IOException => "Io",
            OperationCanceledException => "Canceled",
            _ => "Unexpected"
        };
        var code = (error as BrowserOperationException)?.Code;
        if (code is not null && (code.Length is < 1 or > 100 || code.Any(c => !char.IsAsciiLetterUpper(c) && c != '_'))) code = null;
        var frames = new StackTrace(error, true).GetFrames().Where(f => f.GetMethod()?.DeclaringType?.FullName?.StartsWith("HeadlessScreenReader.", StringComparison.Ordinal) == true)
            .Take(8).Select(f => new PublicTaskFailureFrame(Definition(f.GetMethod()!.DeclaringType!), f.GetMethod()!.Name,
                f.GetFileName() is { } path ? path.Replace('\\', '/').Split('/')[^1] : null, f.GetFileLineNumber(), f.GetILOffset())).ToImmutableArray();
        var value = new PublicTaskFailure(stage, category, code, occurred, Math.Clamp(elapsed, 0, 180000), eventSequence, frames);
        value.Validate(PublicTaskJob.MaximumTabs + 1); return value;
    }
    private static string Definition(Type type) => (type.IsGenericType ? type.GetGenericTypeDefinition() : type).FullName!;
}
