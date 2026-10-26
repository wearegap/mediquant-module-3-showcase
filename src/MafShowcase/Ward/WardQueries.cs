namespace MafShowcase.Ward;

// Fictional data only. Names are obviously invented and MRNs are structurally invalid.
// In MedTrack this interface is the existing application query layer the tools call.

public sealed record PatientSummary(string Mrn, string DisplayName, string Bed);

public sealed record DueMedication(string Medication, decimal DoseValue, string DoseUnit, string Route, DateTimeOffset DueAtUtc);

public interface IWardQueries
{
    IReadOnlyList<PatientSummary> ListPatients(string ward);

    IReadOnlyList<DueMedication> GetDueMedications(string mrn, DateTimeOffset nowUtc);
}

public sealed class FakeWardQueries : IWardQueries
{
    private static readonly Dictionary<string, PatientSummary[]> Patients = new(StringComparer.OrdinalIgnoreCase)
    {
        ["4B"] =
        [
            new("TEST-0001", "Test Patient Alpha", "1"),
            new("TEST-0002", "Test Patient Bravo", "2"),
            new("TEST-0004", "Test Patient Delta", "4"),
        ],
    };

    public IReadOnlyList<PatientSummary> ListPatients(string ward) =>
        Patients.TryGetValue(ward, out var list) ? list : [];

    public IReadOnlyList<DueMedication> GetDueMedications(string mrn, DateTimeOffset nowUtc) => mrn.ToUpperInvariant() switch
    {
        "TEST-0004" =>
        [
            new("Paracetamol", 1, "g", "oral", nowUtc.AddMinutes(20)),
            new("Enoxaparin", 40, "mg", "subcutaneous", nowUtc.AddMinutes(45)),
        ],
        "TEST-0001" => [new("Amoxicillin", 500, "mg", "oral", nowUtc.AddMinutes(10))],
        _ => [],
    };
}
