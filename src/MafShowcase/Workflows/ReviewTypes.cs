using MafShowcase.Tools;

namespace MafShowcase.Workflows;

/// <summary>The messages that travel along the edges. Typed, so the builder can check every edge.</summary>
public sealed record CheckResult(string Check, bool Passed, string Detail);

/// <summary>What the nurse sees before signing: the draft and every check that ran on it.</summary>
public sealed record ReviewCard(AdministrationDraft Draft, List<CheckResult> Checks)
{
    public bool AllPassed => Checks.All(c => c.Passed);
}

/// <summary>The nurse's answer to the sign-off request. The nurse id comes from the server, never from the model.</summary>
public sealed record NurseDecision(bool Approved, string NurseId, string? Reason = null);

/// <summary>Names for the shared state. Executors in the same named scope see each other's values from the next superstep.</summary>
public static class ReviewState
{
    public const string Scope = "review";
    public const string DraftKey = "draft";
}
