using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Cli;

public sealed record ReferenceCase(
    string Id, BundledFixture Fixture, string Task, string Compare, string HsrEvidenceTest);

public enum ReferenceResult { NotRun, Comparable, Different, CannotObserve }

public sealed record ReferenceCaseResult(
    string CaseId, string FixtureSha256, ReferenceResult Result = ReferenceResult.NotRun,
    string? NvdaObservation = null, string? ComparisonNotes = null, string? DifferenceExplanation = null);

public sealed record ReferenceWorksheet(
    int SchemaVersion, string HsrProfile, string? NvdaVersion, string? BrowserVersion,
    string? WindowsVersion, string? NvdaSettings, DateTimeOffset? ObservedAtUtc,
    ImmutableArray<ReferenceCaseResult> Cases);

public sealed record ReferenceAssessment(
    string Status, int Comparable, int Different, int NotRun, int CannotObserve,
    ImmutableArray<string> Issues)
{
    public bool SelectedCasesReviewed => Issues.IsEmpty && NotRun == 0 && CannotObserve == 0;
    public string EvidenceKind => "Operator-supplied observations; selected bundled cases only";
}

/// <summary>Manual reference evidence is never inferred from simulated announcements.</summary>
public static class ReferenceValidation
{
    public const int MaxWorksheetBytes = 1_048_576;
    public static ImmutableArray<ReferenceCase> Cases { get; } =
    [
        new("reading", BundledFixture.Foundation,
            "In browse mode, move to document start, then use H for headings, K for links and D for landmarks.",
            "Record heading levels/order, Read details link, and Primary navigation landmark. Compare semantics, not exact speech wording.",
            "ReferenceFixture_ExposesHeadingLinkAndLandmarkNavigation"),
        new("controls", BundledFixture.Foundation,
            "From document start use B to inspect Open details, the unnamed button and disabled Unavailable. Do not enter Account or Secret fields.",
            "Record unnamed/disabled announcements. NVDA may activate a browse target; HSR requires actual browser focus first. Describe that intentional difference.",
            "Capture_UsesExposedSemanticsAndExcludesInputValues"),
        new("dialog", BundledFixture.Widgets,
            "Tab to Review preferences, Enter; Tab through both dialog buttons in both directions; Escape.",
            "Record dialog name/role, initial Close review focus, containment, and restored Review preferences focus.",
            "ModalDialog_TabStaysInsideAndEscapeRestoresInvoker"),
        new("choices", BundledFixture.Widgets,
            "Tab to Accept terms, Space; Tab to Standard radio, Right Arrow. Ensure focus mode for widget keys.",
            "Record checkbox checked state and Express radio selected state/focus. HSR uses the word checked for radio state.",
            "PreferenceWorkflow_CheckboxRadioAndCombo_SaveThenObserveBlockedRoute"),
        new("combobox", BundledFixture.Widgets,
            "Tab to Delivery region. In focus mode press Down twice, then Enter.",
            "Record expanded state, North then South option announcements, selection, collapse, and retained combobox DOM focus. Do not infer DOM focus from speech alone.",
            "Combobox_TracksActiveDescendantSeparatelyFromDomFocusAndCommitsChoice"),
        new("validation", BundledFixture.Widgets,
            "Leave all fields unchanged, Tab to Save preferences, Enter.",
            "Record Accept terms before saving alert, focus change, and invalid state when returning to the checkbox. HSR does not schedule unsolicited live-region speech.",
            "InvalidForm_ExposesErrorFocusAndWidgetValidationState"),
        new("save-route", BundledFixture.Widgets,
            "Check Accept terms; select Express with Right Arrow; select South with Down twice and Enter; Tab to Save preferences, Enter; wait; Tab to Continue to payment.",
            "Record delayed Preferences saved heading/focus and disabled payment control/description. HSR stops before dispatch to that control; this does not prove all payment routes inaccessible.",
            "PreferenceWorkflow_CheckboxRadioAndCombo_SaveThenObserveBlockedRoute")
    ];

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    static ReferenceValidation() => JsonOptions.MakeReadOnly(populateMissingResolver: true);

    public static ReferenceCase? FindCase(string id) => Cases.FirstOrDefault(item => item.Id == id);

    public static ReferenceWorksheet CreateWorksheet() => new(1, ScreenReaderSession.Profile,
        null, null, null, null, null,
        Cases.Select(item => new ReferenceCaseResult(item.Id,
            BundledFixtureCatalog.Describe(item.Fixture).ContentSha256)).ToImmutableArray());

    public static ReferenceWorksheet ReadWorksheet(Stream input)
    {
        // Read at most one byte beyond the limit, including streams without a Length property.
        var buffer = new byte[MaxWorksheetBytes + 1];
        var count = input.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (count > MaxWorksheetBytes) throw new InvalidDataException("REFERENCE_WORKSHEET_TOO_LARGE");
        var content = buffer.AsSpan(0, count);
        if (content.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) content = content[3..];
        return JsonSerializer.Deserialize<ReferenceWorksheet>(content, JsonOptions)
            ?? throw new InvalidDataException("REFERENCE_WORKSHEET_MISSING");
    }

    public static ReferenceAssessment Assess(ReferenceWorksheet worksheet)
    {
        ArgumentNullException.ThrowIfNull(worksheet);
        var issues = ImmutableArray.CreateBuilder<string>();
        if (worksheet.SchemaVersion != 1) issues.Add("SCHEMA_VERSION_MISMATCH");
        if (worksheet.HsrProfile != ScreenReaderSession.Profile) issues.Add("HSR_PROFILE_MISMATCH");
        if (string.IsNullOrWhiteSpace(worksheet.NvdaVersion)
            || string.IsNullOrWhiteSpace(worksheet.BrowserVersion)
            || string.IsNullOrWhiteSpace(worksheet.WindowsVersion)
            || string.IsNullOrWhiteSpace(worksheet.NvdaSettings)
            || worksheet.ObservedAtUtc is null)
            issues.Add("REFERENCE_ENVIRONMENT_INCOMPLETE");
        var results = worksheet.Cases.IsDefault ? [] : worksheet.Cases;
        if (results.Length != Cases.Length || results.Any(item => item is null)
            || results.Where(item => item is not null).Select(item => item.CaseId).Distinct().Count() != Cases.Length)
            issues.Add("CASE_SET_INCOMPLETE_OR_DUPLICATED");
        foreach (var item in results)
        {
            if (item is null) continue;
            var definition = FindCase(item.CaseId);
            if (definition is null) { issues.Add("UNKNOWN_CASE"); continue; }
            if (item.FixtureSha256 != BundledFixtureCatalog.Describe(definition.Fixture).ContentSha256)
                issues.Add($"FIXTURE_HASH_MISMATCH:{definition.Id}");
            if (!Enum.IsDefined(item.Result)) issues.Add($"UNKNOWN_RESULT:{definition.Id}");
            if (item.Result != ReferenceResult.NotRun
                && (string.IsNullOrWhiteSpace(item.NvdaObservation) || string.IsNullOrWhiteSpace(item.ComparisonNotes)))
                issues.Add($"OBSERVATION_INCOMPLETE:{definition.Id}");
            if (item.Result == ReferenceResult.Different && string.IsNullOrWhiteSpace(item.DifferenceExplanation))
                issues.Add($"DIFFERENCE_UNEXPLAINED:{definition.Id}");
        }
        foreach (var expected in Cases)
            if (!results.Any(item => item?.CaseId == expected.Id)) issues.Add($"CASE_MISSING:{expected.Id}");
        var comparable = results.Count(item => item?.Result == ReferenceResult.Comparable);
        var different = results.Count(item => item?.Result == ReferenceResult.Different);
        var notRun = results.Count(item => item?.Result == ReferenceResult.NotRun);
        var cannotObserve = results.Count(item => item?.Result == ReferenceResult.CannotObserve);
        var status = issues.Count > 0 ? "Incomplete"
            : notRun > 0 || cannotObserve > 0 ? "PendingOperator"
            : different > 0 ? "SelectedCasesReviewedWithDifferences" : "SelectedCasesReviewed";
        return new(status, comparable, different, notRun, cannotObserve, issues.ToImmutable());
    }
}
