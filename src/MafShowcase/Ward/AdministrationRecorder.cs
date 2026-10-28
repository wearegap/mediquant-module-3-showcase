using MafShowcase.Tools;

namespace MafShowcase.Ward;

// Stand-in for MedTrack's existing "record administration" handler from Module 2.
// The tool calls this; it never writes to a database itself.
public interface IAdministrationRecorder
{
    void Record(AdministrationDraft draft, string recordedBy, DateTimeOffset administeredAtUtc);
}

public sealed class FakeAdministrationRecorder : IAdministrationRecorder
{
    public List<AdministrationDraft> Recorded { get; } = [];

    public void Record(AdministrationDraft draft, string recordedBy, DateTimeOffset administeredAtUtc)
    {
        Recorded.Add(draft);
        Console.WriteLine(
            $"[recorder] RECORDED {draft.Medication} {draft.DoseValue} {draft.DoseUnit} {draft.Route} " +
            $"for {draft.Mrn} (order {draft.OrderId}) by {recordedBy} at {administeredAtUtc:HH:mm} UTC");
    }
}

/// <summary>
/// Everything the server decides about one conversation. None of it is ever a tool argument.
/// </summary>
public sealed record WardContext(
    string WardCode,
    string NurseId,
    IWardQueries Queries,
    IAdministrationRecorder Recorder,
    TimeProvider Time)
{
    public static WardContext Demo(IAdministrationRecorder? recorder = null) =>
        new("4B", "nurse-demo", new FakeWardQueries(), recorder ?? new FakeAdministrationRecorder(), TimeProvider.System);
}
