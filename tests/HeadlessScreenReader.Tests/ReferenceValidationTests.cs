using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;

namespace HeadlessScreenReader.Tests;

public sealed class ReferenceValidationTests
{
    [Fact]
    public void Assessment_PreviousReaderProfile_RequiresFreshEvidence()
    {
        foreach (var profile in new[] { "hsr-desktop-reader-v4", "hsr-desktop-reader-v5", "hsr-desktop-reader-v6", "hsr-desktop-reader-v7" })
        {
            var worksheet = CompleteWorksheet() with { HsrProfile = profile };
            var assessment = ReferenceValidation.Assess(worksheet);
            Assert.Contains("HSR_PROFILE_MISMATCH", assessment.Issues);
            Assert.False(assessment.SelectedCasesReviewed);
        }
    }

    [Fact]
    public void Template_LeavesEveryReferenceCaseUnrun()
    {
        var template = ReferenceValidation.CreateWorksheet();
        Assert.Equal(new[] { "reading", "controls", "dialog", "choices", "combobox", "validation", "save-route" },
            template.Cases.Select(item => item.CaseId));
        Assert.All(template.Cases, item =>
        {
            Assert.Equal(ReferenceResult.NotRun, item.Result);
            Assert.Null(item.NvdaObservation);
        });
        var assessment = ReferenceValidation.Assess(template);
        Assert.False(assessment.SelectedCasesReviewed);
        Assert.Equal(7, assessment.NotRun);
        Assert.Equal(0, assessment.Comparable);
        Assert.Contains("REFERENCE_ENVIRONMENT_INCOMPLETE", assessment.Issues);
    }

    [Fact]
    public void Assessment_CompleteObservations_ReviewsOnlySelectedCases()
    {
        var assessment = ReferenceValidation.Assess(CompleteWorksheet());
        Assert.True(assessment.SelectedCasesReviewed);
        Assert.Equal("SelectedCasesReviewed", assessment.Status);
        Assert.Equal(7, assessment.Comparable);
        Assert.Equal(0, assessment.Different);
        Assert.Empty(assessment.Issues);
        Assert.Equal("Operator-supplied observations; selected bundled cases only", assessment.EvidenceKind);
    }

    [Fact]
    public void Assessment_Difference_RequiresExplanationAndPreservesFinding()
    {
        var worksheet = CompleteWorksheet();
        var difference = worksheet.Cases[0] with { Result = ReferenceResult.Different };
        var unexplained = ReferenceValidation.Assess(worksheet with { Cases = worksheet.Cases.SetItem(0, difference) });
        Assert.False(unexplained.SelectedCasesReviewed);
        Assert.Contains("DIFFERENCE_UNEXPLAINED:reading", unexplained.Issues);
        var explained = ReferenceValidation.Assess(worksheet with
        {
            Cases = worksheet.Cases.SetItem(0, difference with { DifferenceExplanation = "Observed speech wording differs; heading level agrees." })
        });
        Assert.True(explained.SelectedCasesReviewed);
        Assert.Equal("SelectedCasesReviewedWithDifferences", explained.Status);
        Assert.Equal(6, explained.Comparable);
        Assert.Equal(1, explained.Different);
    }

    [Theory]
    [InlineData(ReferenceResult.NotRun)]
    [InlineData(ReferenceResult.CannotObserve)]
    public void Assessment_UnfinishedCase_RemainsPending(ReferenceResult result)
    {
        var worksheet = CompleteWorksheet();
        var assessment = ReferenceValidation.Assess(worksheet with
        {
            Cases = worksheet.Cases.SetItem(0, worksheet.Cases[0] with { Result = result })
        });
        Assert.False(assessment.SelectedCasesReviewed);
        Assert.Equal("PendingOperator", assessment.Status);
        Assert.Equal(1, assessment.NotRun + assessment.CannotObserve);
        Assert.Equal(6, assessment.Comparable);
    }

    [Theory]
    [InlineData("schema", "SCHEMA_VERSION_MISMATCH")]
    [InlineData("profile", "HSR_PROFILE_MISMATCH")]
    [InlineData("hash", "FIXTURE_HASH_MISMATCH:reading")]
    [InlineData("environment", "REFERENCE_ENVIRONMENT_INCOMPLETE")]
    [InlineData("browser-version", "REFERENCE_ENVIRONMENT_INCOMPLETE")]
    [InlineData("windows-version", "REFERENCE_ENVIRONMENT_INCOMPLETE")]
    [InlineData("nvda-settings", "REFERENCE_ENVIRONMENT_INCOMPLETE")]
    [InlineData("observation-date", "REFERENCE_ENVIRONMENT_INCOMPLETE")]
    [InlineData("missing", "CASE_MISSING:reading")]
    [InlineData("duplicate", "CASE_SET_INCOMPLETE_OR_DUPLICATED")]
    [InlineData("unknown-case", "UNKNOWN_CASE")]
    [InlineData("observation", "OBSERVATION_INCOMPLETE:reading")]
    [InlineData("comparison", "OBSERVATION_INCOMPLETE:reading")]
    [InlineData("unknown-result", "UNKNOWN_RESULT:reading")]
    [InlineData("null-case", "CASE_SET_INCOMPLETE_OR_DUPLICATED")]
    [InlineData("no-cases", "CASE_MISSING:reading")]
    public void Assessment_InvalidEvidence_CannotComplete(string caseId, string expectedIssue)
    {
        var worksheet = CompleteWorksheet();
        var first = worksheet.Cases[0];
        var invalid = caseId switch
        {
            "schema" => worksheet with { SchemaVersion = 2 },
            "profile" => worksheet with { HsrProfile = "older-profile" },
            "hash" => worksheet with { Cases = worksheet.Cases.SetItem(0, first with { FixtureSha256 = "different-content" }) },
            "environment" => worksheet with { NvdaVersion = " " },
            "browser-version" => worksheet with { BrowserVersion = null },
            "windows-version" => worksheet with { WindowsVersion = " " },
            "nvda-settings" => worksheet with { NvdaSettings = "" },
            "observation-date" => worksheet with { ObservedAtUtc = null },
            "missing" => worksheet with { Cases = worksheet.Cases.RemoveAt(0) },
            "duplicate" => worksheet with { Cases = worksheet.Cases.SetItem(0, worksheet.Cases[1]) },
            "unknown-case" => worksheet with { Cases = worksheet.Cases.SetItem(0, first with { CaseId = "not-in-catalog" }) },
            "observation" => worksheet with { Cases = worksheet.Cases.SetItem(0, first with { NvdaObservation = " " }) },
            "comparison" => worksheet with { Cases = worksheet.Cases.SetItem(0, first with { ComparisonNotes = null }) },
            "unknown-result" => worksheet with { Cases = worksheet.Cases.SetItem(0, first with { Result = (ReferenceResult)99 }) },
            "null-case" => worksheet with { Cases = worksheet.Cases.SetItem(0, null!) },
            "no-cases" => worksheet with { Cases = default },
            _ => throw new ArgumentOutOfRangeException(nameof(caseId))
        };
        var assessment = ReferenceValidation.Assess(invalid);
        Assert.False(assessment.SelectedCasesReviewed);
        Assert.Equal("Incomplete", assessment.Status);
        Assert.Contains(expectedIssue, assessment.Issues);
    }

    [Theory]
    [InlineData("unknown-field")]
    [InlineData("unknown-enum")]
    [InlineData("numeric-enum")]
    public void Parser_UnknownFieldsOrResults_AreRejected(string caseId)
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(ReferenceValidation.CreateWorksheet(), ReferenceValidation.JsonOptions))!;
        if (caseId == "unknown-field") json["AutomaticNvdaPass"] = true;
        else json["Cases"]![0]!["Result"] = caseId == "unknown-enum"
            ? JsonValue.Create("AutomaticPass") : JsonValue.Create(99);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Throws<JsonException>(() => ReferenceValidation.ReadWorksheet(stream));
    }

    [Fact]
    public void Parser_Utf8Bom_PreservesPendingWorksheet()
    {
        var json = JsonSerializer.Serialize(ReferenceValidation.CreateWorksheet(), ReferenceValidation.JsonOptions);
        using var stream = new MemoryStream(new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes(json)).ToArray());
        var worksheet = ReferenceValidation.ReadWorksheet(stream);
        Assert.Equal(1, worksheet.SchemaVersion);
        Assert.Equal("reading", worksheet.Cases[0].CaseId);
        Assert.Equal(ReferenceResult.NotRun, worksheet.Cases[0].Result);
        Assert.False(ReferenceValidation.Assess(worksheet).SelectedCasesReviewed);
    }

    [Fact]
    public void Parser_AtByteLimit_AcceptsValidJson_AboveLimitStopsReading()
    {
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ReferenceValidation.CreateWorksheet(), ReferenceValidation.JsonOptions));
        var bytes = Enumerable.Repeat((byte)' ', ReferenceValidation.MaxWorksheetBytes + 10).ToArray();
        json.CopyTo(bytes, 0);
        using var atLimit = new MemoryStream(bytes, 0, ReferenceValidation.MaxWorksheetBytes, writable: false);
        Assert.Equal(7, ReferenceValidation.ReadWorksheet(atLimit).Cases.Length);
        using var aboveLimit = new MemoryStream(bytes);
        var exception = Assert.Throws<InvalidDataException>(() => ReferenceValidation.ReadWorksheet(aboveLimit));
        Assert.Equal("REFERENCE_WORKSHEET_TOO_LARGE", exception.Message);
        Assert.Equal(ReferenceValidation.MaxWorksheetBytes + 1, aboveLimit.Position);
    }

    [Fact]
    public void Catalog_EvidenceLinksNameExistingTestsAndOnlyBundledFixtures()
    {
        var methods = typeof(ReferenceValidationTests).Assembly.GetTypes().SelectMany(type => type.GetMethods()).Select(method => method.Name).ToHashSet();
        Assert.All(ReferenceValidation.Cases, item =>
        {
            Assert.Contains(item.HsrEvidenceTest, methods);
            Assert.True(Enum.IsDefined(item.Fixture));
        });
        Assert.Null(ReferenceValidation.FindCase("unknown"));
        Assert.Equal(BundledFixture.Widgets, ReferenceValidation.FindCase("dialog")!.Fixture);
        Assert.Throws<ArgumentOutOfRangeException>(() => BundledFixtureCatalog.ReadHtml((BundledFixture)99));
    }

    private static ReferenceWorksheet CompleteWorksheet()
    {
        var worksheet = ReferenceValidation.CreateWorksheet();
        return worksheet with
        {
            NvdaVersion = "synthetic test version; no real AT run",
            BrowserVersion = "synthetic test browser",
            WindowsVersion = "synthetic test Windows",
            NvdaSettings = "synthetic test configuration",
            ObservedAtUtc = DateTimeOffset.Parse("2026-10-17T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            Cases = worksheet.Cases.Select(item => item with
            {
                Result = ReferenceResult.Comparable,
                NvdaObservation = "Synthetic test observation, not a real NVDA result.",
                ComparisonNotes = "Synthetic assertion data, not compatibility evidence."
            }).ToImmutableArray()
        };
    }
}
