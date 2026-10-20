using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Mcp;

public sealed record ToolReply(string Status, string? SessionId, string Profile, object? Data = null, string? ErrorCode = null)
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) <= 131_072) return json;
        // Preserve input uncertainty even when a result cannot fit in the protocol budget.
        object? evidence = Data is ReaderStep step
            ? new { step.BrowserKeystrokes, step.InputMayHaveOccurred, step.Verification, step.Outcome } : null;
        return JsonSerializer.Serialize(new ToolReply("error", SessionId, Profile, evidence, "RESPONSE_TOO_LARGE"), JsonOptions);
    }

    public static ToolReply Error(string code, string? sessionId = null) =>
        new("error", sessionId, ScreenReaderSession.Profile, ErrorCode: code);
}
