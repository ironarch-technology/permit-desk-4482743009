namespace PermitDesk.Api.Domain;

/// <summary>
/// The permit lifecycle as agreed with Development Services. Terminal states
/// accept nothing further; any other state can be withdrawn.
/// </summary>
public static class LifecycleRules
{
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        [ApplicationStates.Draft] = new[] { ApplicationStates.Submitted },
        [ApplicationStates.Submitted] = new[] { ApplicationStates.ZoningCheck },
        [ApplicationStates.ZoningCheck] = new[]
        {
            ApplicationStates.PlanReview, ApplicationStates.Denied, ApplicationStates.ZoningHold
        },
        [ApplicationStates.ZoningHold] = new[] { ApplicationStates.PlanReview, ApplicationStates.Denied },
        [ApplicationStates.PlanReview] = new[]
        {
            ApplicationStates.FeesAssessed, ApplicationStates.Denied, ApplicationStates.CorrectionsRequired
        },
        [ApplicationStates.CorrectionsRequired] = new[] { ApplicationStates.PlanReview },
        [ApplicationStates.FeesAssessed] = new[] { ApplicationStates.FeesAssessed, ApplicationStates.Issued },
        [ApplicationStates.Issued] = new[] { ApplicationStates.Expired },
    };

    private static readonly HashSet<string> Terminal = new()
    {
        ApplicationStates.Denied, ApplicationStates.Withdrawn, ApplicationStates.Expired
    };

    public static bool IsTerminal(string state) => Terminal.Contains(state);

    public static bool CanTransition(string from, string to)
    {
        if (IsTerminal(from)) return false;
        if (to == ApplicationStates.Withdrawn) return true;
        return Allowed.TryGetValue(from, out var targets) && targets.Contains(to);
    }
}
