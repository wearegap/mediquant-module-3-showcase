using MafShowcase.Tools;
using MafShowcase.Ward;

namespace MafShowcase.Guardrails;

/// <summary>
/// Deterministic checks on a draft, against the real order. No model involved.
/// Used twice: before the nurse sees the draft, and again by the tool guard just before the tool runs.
/// </summary>
public sealed class AdministrationValidator(WardContext ward)
{
    public IReadOnlyList<string> Validate(AdministrationDraft draft)
    {
        if (!ward.Queries.ListPatients(ward.WardCode).Any(p => Same(p.Mrn, draft.Mrn)))
        {
            return ["The patient is not on this ward."];
        }

        DueMedication? order = ward.Queries
            .GetDueMedications(draft.Mrn, ward.Time.GetUtcNow())
            .FirstOrDefault(o => Same(o.OrderId, draft.OrderId));

        if (order is null)
        {
            return ["There is no due medication with that order id for this patient."];
        }

        var problems = new List<string>();
        if (!Same(order.Medication, draft.Medication))
        {
            problems.Add($"The medication must be {order.Medication}, as ordered.");
        }
        if (order.DoseValue != draft.DoseValue || !Same(order.DoseUnit, draft.DoseUnit))
        {
            problems.Add($"The dose must be {order.DoseValue} {order.DoseUnit}, as ordered.");
        }
        if (!Same(order.Route, draft.Route))
        {
            problems.Add($"The route must be {order.Route}, as ordered.");
        }
        return problems;
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
