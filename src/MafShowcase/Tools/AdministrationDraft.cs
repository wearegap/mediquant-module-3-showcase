using System.Text.Json;
using Microsoft.Extensions.AI;

namespace MafShowcase.Tools;

/// <summary>
/// The structured draft the model proposes. Its shape is the contract: the tool's parameters are
/// generated from it, the nurse sees it on the approval card, and the validator checks it field by field.
/// </summary>
public sealed record AdministrationDraft(
    string Mrn,
    string OrderId,
    string Medication,
    decimal DoseValue,
    string DoseUnit,
    string Route)
{
    /// <summary>Reads a draft back from the arguments of a record_administration call.</summary>
    public static AdministrationDraft FromArguments(IDictionary<string, object?> arguments)
    {
        JsonElement json = JsonSerializer.SerializeToElement(arguments, AIJsonUtilities.DefaultOptions);
        return json.Deserialize<AdministrationDraft>(JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The tool call has no draft arguments.");
    }

    public override string ToString() =>
        $"{Medication} {DoseValue} {DoseUnit} {Route}, order {OrderId}, patient {Mrn}";
}
