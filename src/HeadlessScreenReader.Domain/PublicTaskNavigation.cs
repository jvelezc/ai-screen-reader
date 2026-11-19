namespace HeadlessScreenReader.Domain;

/// <summary>Why this observed sequential focus route ended; never proof that every possible route was explored.</summary>
public sealed record PublicTaskNavigation(string Strategy, string StopReason, int TabActions,
    int UniqueFocusStates, int? CycleStartSequence)
{
    public const string CurrentStrategy = "GoalOrFocusCycleV1";

    public void Validate(PublicTaskAttempt attempt, PublicTaskGoal goal)
    {
        var tabs = attempt.Events.Count(e => e.Action == "Tab");
        var last = attempt.Events.LastOrDefault();
        var focused = last?.Action == "Enter" ? last.Before : last?.After;
        if (Strategy != CurrentStrategy || attempt.Method != "Keyboard"
            || StopReason is not ("GoalFocused" or "FocusCycle" or "FocusUnobserved" or "InputUnconfirmed" or "TargetChanged" or "NotStarted")
            || TabActions != tabs || UniqueFocusStates < 0 || UniqueFocusStates > (long)tabs + 1
            || (StopReason == "FocusCycle") != (CycleStartSequence is not null)
            || CycleStartSequence is { } at && (at < 0 || at >= tabs)
            || StopReason == "GoalFocused" && focused?.AnchorUrl != goal.DestinationUrl
            || StopReason == "FocusCycle" && (last?.Action != "Tab" || !last.InputCompleted || last.After is null
                || (CycleStartSequence == 0 ? attempt.Events[0].Before : attempt.Events[CycleStartSequence!.Value - 1].After)?.NodeId != last.After.NodeId)
            || StopReason is "FocusCycle" or "FocusUnobserved" or "NotStarted" && attempt.Events.Any(e => e.Action == "Enter"))
            throw new BrowserOperationException("PUBLIC_TASK_NAVIGATION_INVALID");
    }
}
