using MafShowcase.Rag;
using MafShowcase.Ward;
using MafShowcase.Workflows;
using Microsoft.Agents.AI.Workflows;

namespace MafShowcase.Tests;

/// <summary>
/// Session 4. The graph is code, so it is tested like code: the deterministic workflows directly,
/// and feature 0008 with the scripted chat client standing in for both agent steps.
/// </summary>
public sealed class WorkflowTests : IDisposable
{
    private readonly string _database = Path.Combine(Path.GetTempPath(), $"workflow-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (string file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_database) + "*"))
        {
            try { File.Delete(file); } catch (IOException) { /* SQLite may still hold it */ }
        }
    }

    /// <summary>Runs a workflow to its output. Answers every sign-off request with <paramref name="decide"/>.</summary>
    /// <remarks>
    /// The input must keep its static type: RunStreamingAsync&lt;TInput&gt; routes the message by TInput.
    /// Typed as object, no executor handles it and the run ends silently with no output.
    /// </remarks>
    private static async Task<(object? Output, int SignOffRequests)> RunAsync<TInput>(
        Workflow workflow, TInput input, Func<ReviewCard, NurseDecision>? decide = null) where TInput : notnull
    {
        int requests = 0;
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, input);
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case RequestInfoEvent request when request.Request.TryGetDataAs<ReviewCard>(out ReviewCard? card):
                    requests++;
                    Assert.NotNull(decide);
                    await run.SendResponseAsync(request.Request.CreateResponse(decide!(card!)));
                    break;
                case WorkflowOutputEvent output:
                    return (output.Data, requests);
                case WorkflowErrorEvent error:
                    throw new InvalidOperationException("The workflow failed.", error.Exception);
            }
        }
        return (null, requests);
    }

    // ---------- Deterministic workflows (Steps 15 and 16) ----------

    [Fact]
    public async Task Sequential_workflow_turns_a_bed_number_into_the_due_draft()
    {
        (object? output, _) = await RunAsync(RoundWorkflows.Sequential(WardContext.Demo()), "4");

        Assert.Contains("Paracetamol", output?.ToString());
        Assert.Contains("ORD-0004-1", output?.ToString());
    }

    [Fact]
    public async Task Fan_in_waits_for_all_three_checks_and_fails_amoxicillin_with_a_penicillin_allergy()
    {
        (object? output, _) = await RunAsync(RoundWorkflows.ParallelChecks(WardContext.Demo()), "1");

        var card = Assert.IsType<ReviewCard>(output);
        Assert.Equal(3, card.Checks.Count);
        Assert.False(card.AllPassed);
        Assert.Contains(card.Checks, c => c.Check == "Allergy" && !c.Passed);
    }

    // ---------- Feature 0008: draft, check, sign, record ----------

    private static ScriptedChatClient Script(string draftJson) => new(
        ScriptedChatClient.Text(draftJson),                                    // Drafter (structured output)
        ScriptedChatClient.Text("- Check every source of paracetamol [WP-4B-01 p.1]")); // ProtocolCheck

    private const string GoodDraft = """{"orderId":"ORD-0004-1","medication":"Paracetamol","doseValue":1,"doseUnit":"g","route":"oral"}""";
    private const string WrongDose = """{"orderId":"ORD-0004-1","medication":"Paracetamol","doseValue":2,"doseUnit":"g","route":"oral"}""";

    private ProtocolIndex Index() => new(_database, new HashingEmbeddingGenerator());

    [Fact]
    public async Task Approved_draft_is_recorded_once_by_the_nurse_who_signed()
    {
        var recorder = new FakeAdministrationRecorder();
        var ward = WardContext.Demo(recorder);
        using var index = Index();

        (object? output, int requests) = await RunAsync(
            AdministrationReviewWorkflow.Create(Script(GoodDraft), index, ward), "I gave the paracetamol to bed 4.",
            card => new NurseDecision(true, ward.NurseId));

        Assert.Equal(1, requests);
        AdministrationDraftAssert(recorder);
        Assert.StartsWith("Recorded:", output?.ToString());
    }

    [Fact]
    public async Task Rejected_draft_records_nothing()
    {
        var recorder = new FakeAdministrationRecorder();
        var ward = WardContext.Demo(recorder);
        using var index = Index();

        (object? output, int requests) = await RunAsync(
            AdministrationReviewWorkflow.Create(Script(GoodDraft), index, ward), "I gave the paracetamol to bed 4.",
            card => new NurseDecision(false, ward.NurseId, "Wrong patient."));

        Assert.Equal(1, requests);
        Assert.Empty(recorder.Recorded);
        Assert.StartsWith("Not recorded:", output?.ToString());
    }

    [Fact]
    public async Task Draft_that_fails_the_order_check_is_never_offered_for_sign_off()
    {
        var recorder = new FakeAdministrationRecorder();
        var ward = WardContext.Demo(recorder);
        using var index = Index();

        (object? output, int requests) = await RunAsync(
            AdministrationReviewWorkflow.Create(Script(WrongDose), index, ward), "I gave 2 g of paracetamol to bed 4.");

        Assert.Equal(0, requests);
        Assert.Empty(recorder.Recorded);
        Assert.StartsWith("Not offered for sign-off:", output?.ToString());
    }

    [Fact]
    public async Task The_model_never_sees_patient_names_or_mrns()
    {
        var ward = WardContext.Demo();
        using var index = Index();
        ScriptedChatClient chat = Script(GoodDraft);

        await RunAsync(AdministrationReviewWorkflow.Create(chat, index, ward), "I gave the paracetamol to bed 4.",
            card => new NurseDecision(false, ward.NurseId));

        string everything = string.Join("\n", chat.Requests.SelectMany(r => r).Select(m => m.Text));
        Assert.DoesNotContain("TEST-", everything);
        Assert.DoesNotContain("Test Patient", everything);
    }

    [Fact]
    public async Task Review_runner_pauses_resumes_by_id_and_refuses_a_second_decision()
    {
        var recorder = new FakeAdministrationRecorder();
        var ward = WardContext.Demo(recorder);
        using var index = Index();
        var runner = new ReviewRunner();

        ReviewOutcome started = await runner.StartAsync(AdministrationReviewWorkflow.Create(Script(GoodDraft), index, ward), "I gave the paracetamol to bed 4.");
        Assert.NotNull(started.PendingId);
        Assert.Empty(recorder.Recorded);                                   // nothing before the signature

        ReviewOutcome? decided = await runner.DecideAsync(started.PendingId!, new NurseDecision(true, ward.NurseId));
        Assert.StartsWith("Recorded:", decided?.Result);
        Assert.Single(recorder.Recorded);

        Assert.Null(await runner.DecideAsync(started.PendingId!, new NurseDecision(true, ward.NurseId)));   // 404
        Assert.Single(recorder.Recorded);
    }

    private static void AdministrationDraftAssert(FakeAdministrationRecorder recorder)
    {
        var recorded = Assert.Single(recorder.Recorded);
        Assert.Equal("TEST-0004", recorded.Mrn);
        Assert.Equal(1m, recorded.DoseValue);
    }
}
