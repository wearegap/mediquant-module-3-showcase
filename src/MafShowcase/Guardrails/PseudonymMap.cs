using System.Text.RegularExpressions;
using MafShowcase.Ward;

namespace MafShowcase.Guardrails;

/// <summary>
/// One conversation's mapping between real identifiers and placeholders.
/// "Test Patient Delta" becomes PATIENT_3 and "TEST-0004" becomes MRN_3 before anything reaches the model,
/// and the placeholders are swapped back in what the model returns.
/// It redacts what the server knows (the ward's patients) plus anything shaped like an MRN.
/// It cannot recognise a name it was never told about: that limit is part of the design, not a bug.
/// </summary>
public sealed partial class PseudonymMap
{
    public const string UnknownMrnToken = "MRN_UNKNOWN";

    private readonly Dictionary<string, string> _realToToken = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _tokenToReal = new(StringComparer.Ordinal);
    private readonly Regex _mrnPattern;
    private int _count;

    public PseudonymMap(Regex mrnPattern) => _mrnPattern = mrnPattern;

    public static PseudonymMap ForPatients(IEnumerable<PatientSummary> patients, Regex mrnPattern)
    {
        var map = new PseudonymMap(mrnPattern);
        foreach (var patient in patients)
        {
            map.Add(patient);
        }
        return map;
    }

    /// <summary>Adds a patient without renumbering existing ones, so placeholders stay stable for the whole conversation.</summary>
    public void Add(PatientSummary patient)
    {
        if (_realToToken.ContainsKey(patient.Mrn))
        {
            return;
        }

        _count++;
        Register(patient.Mrn, $"MRN_{_count}");
        Register(patient.DisplayName, $"PATIENT_{_count}");
    }

    /// <summary>Real values to placeholders. Applied to everything sent to the model.</summary>
    public string Pseudonymise(string text)
    {
        // Longest first, so a full name is replaced before any shorter value it contains.
        foreach (var (real, token) in _realToToken.OrderByDescending(p => p.Key.Length))
        {
            text = text.Replace(real, token, StringComparison.OrdinalIgnoreCase);
        }

        // Backstop: an MRN we were not told about (another ward, a typo) never leaves either.
        return _mrnPattern.Replace(text, UnknownMrnToken);
    }

    /// <summary>Placeholders back to real values. Applied to everything the model returns.</summary>
    public string Restore(string text) =>
        TokenPattern().Replace(text, m =>
            m.Value == UnknownMrnToken ? UnknownMrnText
            : _tokenToReal.TryGetValue(m.Value, out var real) ? real
            : m.Value);

    /// <summary>True if the text still holds a placeholder this conversation never issued (the model invented one).</summary>
    public bool ContainsUnresolvedToken(string text) =>
        TokenPattern().Matches(text).Any(m => m.Value != UnknownMrnToken && !_tokenToReal.ContainsKey(m.Value));

    /// <summary>What the nurse reads where the model wrote MRN_UNKNOWN.</summary>
    public const string UnknownMrnText = "(an MRN that is not on this ward)";

    /// <summary>How many characters at the end of a streamed chunk might be the start of a placeholder.</summary>
    public static int PartialTokenSuffixLength(string text) => PartialTokenAtEnd().Match(text) is { Success: true } m ? m.Length : 0;

    private void Register(string real, string token)
    {
        _realToToken[real] = token;
        _tokenToReal[token] = real;
    }

    [GeneratedRegex(@"\b(?:PATIENT|MRN)_(?:\d+|UNKNOWN)\b")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"(?:P(?:A(?:T(?:I(?:E(?:N(?:T(?:_\d*)?)?)?)?)?)?)?|M(?:R(?:N(?:_\d*)?)?)?)$")]
    private static partial Regex PartialTokenAtEnd();
}

/// <summary>
/// Restores placeholders in a stream. A placeholder can be split across chunks ("PATI" + "ENT_3"),
/// so a possible partial placeholder at the end of a chunk is held back until the next chunk arrives.
/// </summary>
public sealed class StreamingRestorer(PseudonymMap map)
{
    private string _pending = "";

    public string Push(string chunk)
    {
        var text = _pending + chunk;
        var hold = PseudonymMap.PartialTokenSuffixLength(text);
        _pending = text[^hold..];
        return map.Restore(text[..^hold]);
    }

    public string Flush()
    {
        var rest = map.Restore(_pending);
        _pending = "";
        return rest;
    }
}
