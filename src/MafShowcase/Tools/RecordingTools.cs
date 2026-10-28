using System.ComponentModel;
using MafShowcase.Ward;
using Microsoft.Extensions.AI;

namespace MafShowcase.Tools;

/// <summary>
/// The only write tool. It is wrapped in ApprovalRequiredAIFunction, so the framework stops and asks
/// the caller before it runs. Who records and when are decided by the server, never by the model.
/// </summary>
public sealed class RecordingTools(WardContext ward)
{
    public const string RecordAdministrationName = "record_administration";

    [Description("Records that a due medication was given to a patient on this ward. " +
                 "Use the values exactly as get_due_medications returned them. " +
                 "A nurse must approve the draft before anything is recorded.")]
    public string RecordAdministration(
        [Description("The patient's MRN, as returned by list_ward_patients.")] string mrn,
        [Description("The order id of the due medication, as returned by get_due_medications.")] string orderId,
        [Description("The medication name, as returned by get_due_medications.")] string medication,
        [Description("The dose value exactly as ordered.")] decimal doseValue,
        [Description("The dose unit exactly as ordered, for example mg or g.")] string doseUnit,
        [Description("The route exactly as ordered, for example oral.")] string route)
    {
        var draft = new AdministrationDraft(mrn, orderId, medication, doseValue, doseUnit, route);

        // Reuse the application's existing handler. The nurse and the time come from the server.
        ward.Recorder.Record(draft, recordedBy: ward.NurseId, administeredAtUtc: ward.Time.GetUtcNow());

        return $"Recorded: {draft}.";
    }

    public IList<AITool> AsAITools() =>
    [
        new ApprovalRequiredAIFunction(
            AIFunctionFactory.Create(RecordAdministration, name: RecordAdministrationName)),
    ];
}
