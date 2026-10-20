using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumTableTests
{
    [Fact]
    public async Task Tables_Chromium_NativeGeometryHeadersAndCellMovementAreReadOnly()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Tables);
        var before = await browser.CaptureAsync();
        using var reader = new ScreenReaderSession(browser);
        var table = await FindTable(reader, "Products");
        Assert.Equal(new(3, 2, null, null, null, true, null, SemanticSource.NativeHtmlProperty), table.Table!.Geometry);
        var first = await reader.ExecuteAsync(ReaderCommand.NextCell);
        Assert.StartsWith("Product, columnheader", first.Announcement, StringComparison.Ordinal);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.PreviousCell)).Outcome);
        var header = await reader.ExecuteAsync(ReaderCommand.NextColumn);
        var value = await reader.ExecuteAsync(ReaderCommand.NextRow);
        Assert.Equal("$3, cell, native table 3 rows, 2 columns, row 2, column 2, row header Apples, column header Price", value.Announcement);
        Assert.Equal("Apples", Assert.Single(value.Table!.RowHeaders).Name);
        Assert.Equal(SemanticSource.NativeHtmlProperty, Assert.Single(value.Table.ColumnHeaders).Source);
        Assert.Equal(2, value.Table.Geometry.RowIndex);
        Assert.Equal(2, value.Table.Geometry.ColumnIndex);
        var last = await reader.ExecuteAsync(ReaderCommand.NextRow);
        Assert.StartsWith("$4, cell", last.Announcement, StringComparison.Ordinal);
        var boundary = await reader.ExecuteAsync(ReaderCommand.NextCell);
        Assert.Equal(ReaderOutcome.NoMatch, boundary.Outcome);
        Assert.Equal(last.CursorNodeId, boundary.CursorNodeId);
        Assert.Equal(value.CursorNodeId, (await reader.ExecuteAsync(ReaderCommand.PreviousRow)).CursorNodeId);
        Assert.Equal(header.CursorNodeId, (await reader.ExecuteAsync(ReaderCommand.PreviousRow)).CursorNodeId);
        Assert.Equal(first.CursorNodeId, (await reader.ExecuteAsync(ReaderCommand.PreviousColumn)).CursorNodeId);
        var after = await browser.CaptureAsync();
        Assert.Equal(before.FocusedNode?.Id, after.FocusedNode?.Id);
        Assert.Equal(before.Nodes.Select(n => n.Id), after.Nodes.Select(n => n.Id));
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    [Theory]
    [InlineData("Missing headers", "Plain A")]
    [InlineData("Unnamed header", "Unlabelled row")]
    public async Task Tables_Chromium_MissingOrUnnamedHeadersRemainUnknown(string tableName, string value)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Tables);
        using var reader = new ScreenReaderSession(browser);
        await FindTable(reader, tableName);
        var cell = await reader.ExecuteAsync(ReaderCommand.NextCell);
        if (tableName == "Unnamed header") cell = await reader.ExecuteAsync(ReaderCommand.NextRow);
        Assert.StartsWith(value + ", cell", cell.Announcement, StringComparison.Ordinal);
        Assert.Empty(cell.Table!.RowHeaders);
        Assert.Empty(cell.Table.ColumnHeaders);
        Assert.Contains("row header unknown, column header unknown", cell.Announcement, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tables_Chromium_HiddenContentIsExcludedAndNativePositionsAreNotExposedCounts()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Tables);
        using var reader = new ScreenReaderSession(browser);
        await FindTable(reader, "Hidden content");
        var header = await reader.ExecuteAsync(ReaderCommand.NextCell);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextColumn)).Outcome);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextRow)).Outcome);
        var value = await reader.ExecuteAsync(ReaderCommand.NextCell);
        Assert.StartsWith("Visible value, cell", value.Announcement, StringComparison.Ordinal);
        Assert.Equal(3, value.Table!.Geometry.RowIndex);
        Assert.Equal(3, value.Table.Geometry.RowCount);
        Assert.Equal(header.CursorNodeId, Assert.Single(value.Table.ColumnHeaders).NodeId);
        var unknown = await reader.ExecuteAsync(ReaderCommand.NextColumn);
        Assert.Empty(unknown.Table!.ColumnHeaders);
        Assert.Contains("column header unknown", unknown.Announcement, StringComparison.Ordinal);
        var serialized = JsonSerializer.Serialize(await browser.CaptureAsync());
        Assert.DoesNotContain("HIDDEN_HEADER_CANARY", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("HIDDEN_ROW_CANARY", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    [Theory]
    [InlineData("Outer", "TABLE_NESTING_UNSUPPORTED")]
    [InlineData("Merged", "TABLE_SPAN_UNSUPPORTED")]
    [InlineData("Editable grid", "TABLE_ROLE_OVERRIDE_UNSUPPORTED")]
    [InlineData("Virtual", "TABLE_DECLARED_GEOMETRY_UNSUPPORTED")]
    [InlineData("Declared headers", "TABLE_EXPLICIT_HEADERS_UNSUPPORTED")]
    public async Task Tables_Chromium_UnsupportedLayoutsNeverReceiveGuessedNavigation(string name, string reason)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Tables);
        using var reader = new ScreenReaderSession(browser);
        var table = await FindTable(reader, name);
        Assert.False(table.Table!.Geometry.IsSupported);
        Assert.Equal(reason, table.Table.Geometry.ReasonCode);
        var cell = await reader.ExecuteAsync(ReaderCommand.NextCell);
        Assert.Equal(ReaderOutcome.Unsupported, cell.Outcome);
        Assert.Equal(table.CursorNodeId, cell.CursorNodeId);
        Assert.Null(cell.BarrierCode);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public async Task Tables_Chromium_NestedTableUsesOnlyItsOwnHeaders()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Tables);
        using var reader = new ScreenReaderSession(browser);
        var inner = await FindTable(reader, "Inner");
        await reader.ExecuteAsync(ReaderCommand.NextCell);
        var cell = await reader.ExecuteAsync(ReaderCommand.NextRow);
        Assert.StartsWith("Inner value, cell", cell.Announcement, StringComparison.Ordinal);
        Assert.Equal(inner.CursorNodeId, cell.Table!.TableNodeId);
        Assert.Equal("Inner header", Assert.Single(cell.Table.ColumnHeaders).Name);
        Assert.DoesNotContain("Outer header", cell.Announcement, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tables_Chromium_ChildDocumentOwnershipExcludesParentHeaders()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Tables);
        using var reader = new ScreenReaderSession(browser);
        var main = await FindTable(reader, "Products");
        var child = await FindTable(reader, "Child products");
        await reader.ExecuteAsync(ReaderCommand.NextCell);
        var value = await reader.ExecuteAsync(ReaderCommand.NextRow);
        Assert.NotEqual(main.Frame!.DocumentId, child.Frame!.DocumentId);
        Assert.Equal(child.Frame.DocumentId, value.Frame!.DocumentId);
        Assert.Equal(child.CursorNodeId, value.Table!.TableNodeId);
        Assert.Equal("Child header", Assert.Single(value.Table.ColumnHeaders).Name);
        Assert.Empty(value.Table.RowHeaders);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextRow)).Outcome);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextTable)).Outcome);
        Assert.StartsWith("Declared headers, table", (await reader.ExecuteAsync(ReaderCommand.PreviousTable)).Announcement, StringComparison.Ordinal);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public async Task Tables_Chromium_ExternalRemovalInvalidatesStaleCellAndRefreshesHeaders()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Tables);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.Tab);
        await FindTable(reader, "Products");
        await reader.ExecuteAsync(ReaderCommand.NextCell);
        await reader.ExecuteAsync(ReaderCommand.NextColumn);
        var apples = await reader.ExecuteAsync(ReaderCommand.NextRow);
        await browser.PressKeyAsync("Enter");
        var stale = await reader.ExecuteAsync(ReaderCommand.NextCell);
        Assert.Equal(ReaderOutcome.Unknown, stale.Outcome);
        Assert.Null(stale.CursorNodeId);
        Assert.Null(stale.Table);
        var snapshot = await browser.CaptureAsync();
        Assert.DoesNotContain(snapshot.Nodes, n => n.Id == apples.CursorNodeId || n.Name == "Apples");
        await FindTable(reader, "Products");
        await reader.ExecuteAsync(ReaderCommand.NextCell);
        await reader.ExecuteAsync(ReaderCommand.NextColumn);
        var pears = await reader.ExecuteAsync(ReaderCommand.NextRow);
        Assert.StartsWith("$4, cell", pears.Announcement, StringComparison.Ordinal);
        Assert.Equal(2, pears.Table!.Geometry.RowCount);
        Assert.Equal("Pears", Assert.Single(pears.Table.RowHeaders).Name);
        Assert.Contains(reader.Timeline, e => e.Kind == ReaderEventKind.WidgetStateChanged && e.Table?.RowCount == 2);
    }

    private static async Task<ReaderStep> FindTable(ScreenReaderSession reader, string name)
    {
        for (var i = 0; i < 16; i++)
        {
            var step = await reader.ExecuteAsync(ReaderCommand.NextTable);
            if (step.Announcement.StartsWith(name + ", ", StringComparison.Ordinal)) return step;
            Assert.Equal(ReaderOutcome.CursorMoved, step.Outcome);
        }
        throw new InvalidOperationException("Expected bundled table was not found.");
    }
}
