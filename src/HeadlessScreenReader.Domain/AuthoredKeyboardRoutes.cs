using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>The complete allowed route catalog; no caller-defined commands or target predicates.</summary>
public static class AuthoredKeyboardRoutes
{
    public static ImmutableArray<TraversalPlan> Plans =>
    [
        new("Successful checkout preview", new("heading", "Synthetic shop"), new("heading", "Checkout preview", MustBeFocused: true),
            [new(ReaderCommand.Tab), new(ReaderCommand.Activate)]),
        new("Disabled checkout route", new("heading", "Synthetic shop"), new("heading", "Checkout preview"),
            [new(ReaderCommand.DocumentEnd), new(ReaderCommand.PreviousButton), new(ReaderCommand.Activate)]),
        new("Validation dialog instead of checkout", new("heading", "Synthetic shop"), new("heading", "Checkout preview"),
            [new(ReaderCommand.Tab), new(ReaderCommand.Tab), new(ReaderCommand.Activate)]),
        new("Unchanged checkout route", new("heading", "Synthetic shop"), new("heading", "Checkout preview"),
            [new(ReaderCommand.Tab), new(ReaderCommand.Tab), new(ReaderCommand.Tab), new(ReaderCommand.Activate)])
    ];
}
