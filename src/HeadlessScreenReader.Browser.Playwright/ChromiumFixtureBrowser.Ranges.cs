using System.Globalization;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed partial class ChromiumFixtureBrowser
{
    private static bool IsRangeRole(string role) => role is "slider" or "meter" or "progressbar";

    private static RangeObservation? ReadRange(JsonElement node, string role, Dictionary<string, string> properties)
    {
        if (!IsRangeRole(role) || properties.ContainsKey("editable")) return null;
        static double? Number(string? text) => double.TryParse(text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
        var text = properties.GetValueOrDefault("valuetext");
        return new(Number(Value(node, "value")), Number(properties.GetValueOrDefault("valuemin")),
            Number(properties.GetValueOrDefault("valuemax")), SemanticSource.ChromiumAccessibility,
            string.IsNullOrWhiteSpace(text) ? null : new(text, SemanticSource.ChromiumAccessibility));
    }
}
