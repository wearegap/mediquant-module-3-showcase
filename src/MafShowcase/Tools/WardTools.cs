using System.ComponentModel;
using MafShowcase.Ward;
using Microsoft.Extensions.AI;

namespace MafShowcase.Tools;

/// <summary>
/// Read-only tools. Each one delegates to the existing query layer and returns small DTOs.
/// The ward is fixed by the server when the tools are created: the model can never choose it.
/// Every argument the model does choose is untrusted input.
/// </summary>
public sealed class WardTools(IWardQueries queries, TimeProvider timeProvider, string wardCode)
{
    [Description("Lists the patients currently on this ward, with their MRN and bed number.")]
    public IReadOnlyList<PatientSummary> ListWardPatients() =>
        queries.ListPatients(wardCode);

    [Description("Gets the medications due for one patient on this ward in the current administration window.")]
    public IReadOnlyList<DueMedication> GetDueMedications(
        [Description("The patient's MRN, as returned by list_ward_patients.")] string mrn)
    {
        // The model may pass any MRN. Only answer for patients on this ward.
        var onWard = queries.ListPatients(wardCode).Any(p => string.Equals(p.Mrn, mrn, StringComparison.OrdinalIgnoreCase));
        return onWard ? queries.GetDueMedications(mrn, timeProvider.GetUtcNow()) : [];
    }

    public IList<AITool> AsAITools() =>
    [
        AIFunctionFactory.Create(ListWardPatients, name: "list_ward_patients"),
        AIFunctionFactory.Create(GetDueMedications, name: "get_due_medications"),
    ];
}
