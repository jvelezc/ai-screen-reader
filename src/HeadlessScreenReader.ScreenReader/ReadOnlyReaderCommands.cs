using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.ScreenReader;

/// <summary>Reviewed pure browse commands; new Domain commands remain denied until explicitly added.</summary>
public static class ReadOnlyReaderCommands
{
    public static readonly ImmutableDictionary<string, ReaderCommand> Allowed = new[]
    {
        ReaderCommand.ReadCurrent, ReaderCommand.MoveNext, ReaderCommand.MovePrevious,
        ReaderCommand.NextHeading, ReaderCommand.PreviousHeading, ReaderCommand.NextLink, ReaderCommand.PreviousLink,
        ReaderCommand.NextButton, ReaderCommand.PreviousButton, ReaderCommand.NextLandmark, ReaderCommand.PreviousLandmark,
        ReaderCommand.DocumentStart, ReaderCommand.DocumentEnd, ReaderCommand.EnterBrowseMode,
        ReaderCommand.ReadCurrentText, ReaderCommand.NextCharacter, ReaderCommand.PreviousCharacter,
        ReaderCommand.NextWord, ReaderCommand.PreviousWord, ReaderCommand.NextLine, ReaderCommand.PreviousLine,
        ReaderCommand.NextFrame, ReaderCommand.PreviousFrame, ReaderCommand.NextRange, ReaderCommand.PreviousRange,
        ReaderCommand.NextTree, ReaderCommand.PreviousTree, ReaderCommand.NextTreeItem, ReaderCommand.PreviousTreeItem,
        ReaderCommand.NextTabItem, ReaderCommand.PreviousTabItem, ReaderCommand.NextTable, ReaderCommand.PreviousTable,
        ReaderCommand.NextCell, ReaderCommand.PreviousCell, ReaderCommand.NextRow, ReaderCommand.PreviousRow,
        ReaderCommand.NextColumn, ReaderCommand.PreviousColumn, ReaderCommand.ReadCurrentParagraph,
        ReaderCommand.NextParagraph, ReaderCommand.PreviousParagraph, ReaderCommand.NextListbox,
        ReaderCommand.PreviousListbox, ReaderCommand.NextOption, ReaderCommand.PreviousOption
    }.ToImmutableDictionary(command => command.ToString(), StringComparer.Ordinal);
}
