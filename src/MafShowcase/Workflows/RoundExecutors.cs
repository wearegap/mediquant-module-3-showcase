using MafShowcase.Guardrails;
using MafShowcase.Tools;
using MafShowcase.Ward;
using Microsoft.Agents.AI.Workflows;

namespace MafShowcase.Workflows;

// Plain C# executors. No model anywhere: a workflow is a graph of steps, and most steps are ordinary code.
// Each executor has a fixed id (the base constructor argument): events, diagrams and checkpoints use it.

/// <summary>Bed number in, patient out.</summary>
public sealed class FindPatientExecutor(WardContext ward) : Executor<string, PatientSummary>("FindPatient")
{
    public override ValueTask<PatientSummary> HandleAsync(string bed, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        PatientSummary patient = ward.Queries.ListPatients(ward.WardCode).FirstOrDefault(p => p.Bed == bed.Trim())
            ?? throw new InvalidOperationException($"No patient in bed {bed} on ward {ward.WardCode}.");
        return ValueTask.FromResult(patient);
    }
}

/// <summary>
/// Patient in, the next due order as a draft out. The draft also goes into shared state because Step 16's aggregator
/// reads it back after the fan-in. Step 15 doesn't need it: its next step gets the draft as a message.
/// </summary>
public sealed class FindDueOrderExecutor(WardContext ward) : Executor<PatientSummary, AdministrationDraft>("FindDueOrder")
{
    public override async ValueTask<AdministrationDraft> HandleAsync(PatientSummary patient, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        DueMedication order = ward.Queries.GetDueMedications(patient.Mrn, ward.Time.GetUtcNow()).OrderBy(o => o.DueAtUtc).FirstOrDefault()
            ?? throw new InvalidOperationException($"Nothing is due for bed {patient.Bed}.");

        var draft = new AdministrationDraft(patient.Mrn, order.OrderId, order.Medication, order.DoseValue, order.DoseUnit, order.Route);
        await context.QueueStateUpdateAsync(ReviewState.DraftKey, draft, scopeName: ReviewState.Scope, cancellationToken);
        return draft;
    }
}

/// <summary>The Session 2 validator as a workflow step: the draft must match the real order.</summary>
public sealed class OrderCheckExecutor(WardContext ward) : Executor<AdministrationDraft, CheckResult>("OrderCheck")
{
    public override ValueTask<CheckResult> HandleAsync(AdministrationDraft draft, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> problems = new AdministrationValidator(ward).Validate(draft);
        return ValueTask.FromResult(problems.Count == 0
            ? new CheckResult("Order", true, "Matches the order.")
            : new CheckResult("Order", false, string.Join(" ", problems)));
    }
}

/// <summary>WP-4B-06: non-critical medicines within 60 minutes of the scheduled time.</summary>
public sealed class DueWindowCheckExecutor(WardContext ward) : Executor<AdministrationDraft, CheckResult>("DueWindowCheck")
{
    public override ValueTask<CheckResult> HandleAsync(AdministrationDraft draft, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = ward.Time.GetUtcNow();
        DueMedication? order = ward.Queries.GetDueMedications(draft.Mrn, now).FirstOrDefault(o => o.OrderId == draft.OrderId);
        if (order is null)
        {
            return ValueTask.FromResult(new CheckResult("Due time", false, "No such due order."));
        }

        double minutes = Math.Abs((order.DueAtUtc - now).TotalMinutes);
        return ValueTask.FromResult(minutes <= 60
            ? new CheckResult("Due time", true, $"Due in {minutes:F0} minutes, inside the 60-minute window.")
            : new CheckResult("Due time", false, $"Due in {minutes:F0} minutes, outside the 60-minute window (WP-4B-06)."));
    }
}

/// <summary>WP-4B-08 and WP-4B-03: an allergy status must be recorded, and a penicillin allergy blocks amoxicillin.</summary>
public sealed class AllergyCheckExecutor() : Executor<AdministrationDraft, CheckResult>("AllergyCheck")
{
    // Fictional allergy records. In MedTrack this is the existing allergy query.
    private static readonly Dictionary<string, string> Allergies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TEST-0004"] = "No known allergies",
        ["TEST-0001"] = "Penicillin (rash)",
    };

    public override ValueTask<CheckResult> HandleAsync(AdministrationDraft draft, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        if (!Allergies.TryGetValue(draft.Mrn, out string? recorded))
        {
            return ValueTask.FromResult(new CheckResult("Allergy", false, "Allergy status not recorded (WP-4B-08)."));
        }
        bool penicillinClash = recorded.Contains("penicillin", StringComparison.OrdinalIgnoreCase)
            && draft.Medication.Equals("Amoxicillin", StringComparison.OrdinalIgnoreCase);
        return ValueTask.FromResult(penicillinClash
            ? new CheckResult("Allergy", false, $"Recorded: {recorded}. Do not give amoxicillin (WP-4B-03).")
            : new CheckResult("Allergy", true, $"Recorded: {recorded}."));
    }
}

/// <summary>
/// The fan-in target. The barrier delivers every check's result in the same superstep; this executor counts them,
/// reads the draft from shared state, and sends one review card. Built fresh per run, so its counter never leaks.
/// </summary>
[SendsMessage(typeof(ReviewCard))]
[YieldsOutput(typeof(ReviewCard))]
public sealed class ReviewAggregatorExecutor(int expectedChecks, bool yieldAsOutput) : Executor<CheckResult>("ReviewAggregator")
{
    private readonly List<CheckResult> _results = [];

    public override async ValueTask HandleAsync(CheckResult result, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        _results.Add(result);
        if (_results.Count < expectedChecks)
        {
            return;
        }

        AdministrationDraft draft = await context.ReadStateAsync<AdministrationDraft>(ReviewState.DraftKey, scopeName: ReviewState.Scope, cancellationToken)
            ?? throw new InvalidOperationException("No draft in shared state.");
        var card = new ReviewCard(draft, [.. _results.OrderBy(r => r.Check)]);

        if (yieldAsOutput)
        {
            await context.YieldOutputAsync(card, cancellationToken);
        }
        else
        {
            await context.SendMessageAsync(card, cancellationToken: cancellationToken);
        }
    }
}

/// <summary>Step 15 and 16 workflows, built fresh for every run (the docs recommend a factory per run).</summary>
public static class RoundWorkflows
{
    /// <summary>Step 15: three steps in a line. Bed number in, a one-line draft out.</summary>
    public static Workflow Sequential(WardContext ward)
    {
        var findPatient = new FindPatientExecutor(ward);
        var findOrder = new FindDueOrderExecutor(ward);
        Func<AdministrationDraft, string> describe = draft => $"Draft: {draft}";   // a function can be an executor too
        var describeStep = describe.BindAsExecutor("Describe");   // BindAsExecutor for functions; BindExecutor is for executor factories

        return new WorkflowBuilder(findPatient)
            .AddEdge(findPatient, findOrder)
            .AddEdge(findOrder, describeStep)
            .WithOutputFrom(describeStep)
            .Build();
    }

    /// <summary>Step 16: the draft fans out to three independent checks; a barrier fans them back in.</summary>
    public static Workflow ParallelChecks(WardContext ward)
    {
        var findPatient = new FindPatientExecutor(ward);
        var findOrder = new FindDueOrderExecutor(ward);
        var orderCheck = new OrderCheckExecutor(ward);
        var dueCheck = new DueWindowCheckExecutor(ward);
        var allergyCheck = new AllergyCheckExecutor();
        ExecutorBinding[] checks = [orderCheck, dueCheck, allergyCheck];   // typed array: picks the (sources, target) overload
        var aggregator = new ReviewAggregatorExecutor(expectedChecks: checks.Length, yieldAsOutput: true);

        return new WorkflowBuilder(findPatient)
            .AddEdge(findPatient, findOrder)
            .AddFanOutEdge(findOrder, checks)
            .AddFanInBarrierEdge(checks, aggregator)
            .WithOutputFrom(aggregator)
            .Build();
    }
}
