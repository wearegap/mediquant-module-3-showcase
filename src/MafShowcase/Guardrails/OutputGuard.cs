using System.Text.RegularExpressions;

namespace MafShowcase.Guardrails;

/// <summary>
/// The output guardrail: deterministic checks on what the nurse is about to read.
/// It catches two things instructions cannot promise: an identifier that is not on this ward
/// (invented or leaked), and a placeholder the model made up that cannot be restored.
/// </summary>
public sealed class OutputGuard(PseudonymMap map, Regex mrnPattern, IEnumerable<string> wardMrns)
{
    public const string Withdrawn =
        "This answer was withdrawn by a safety check. Please ask again, or use the ward view.";

    private readonly HashSet<string> _wardMrns = new(wardMrns, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the reason the text must not be shown, or null when it is fine.</summary>
    public string? FindViolation(string text)
    {
        if (map.ContainsUnresolvedToken(text))
        {
            return "a placeholder the conversation never issued";
        }

        Match? foreign =mrnPattern.Matches(text).FirstOrDefault(m => !_wardMrns.Contains(m.Value));
        return foreign is null ? null : $"an MRN that is not on this ward ({foreign.Value})";
    }
}
