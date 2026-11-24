namespace NekoPcbEmulator.ProtocolTests;

/// <summary>Runs named cases, collecting results instead of stopping at the first failure.</summary>
internal abstract class CaseRunner
{
    public List<CaseResult> Results { get; } = [];

    protected void Case(string group, string name, Action action)
    {
        try
        {
            action();
            Results.Add(new(group, name, true, null));
            Console.WriteLine($"PASS {group}: {name}");
        }
        catch (Exception ex)
        {
            Results.Add(new(group, name, false, ex.Message));
            Console.WriteLine($"FAIL {group}: {name}: {ex.Message}");
            OnCaseFailed(group);
        }
    }

    /// <summary>Lets a runner discard leftovers so one failure does not cascade into the next case.</summary>
    protected virtual void OnCaseFailed(string group) { }

    protected static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    protected static void Equal<T>(T actual, T expected) =>
        Require(EqualityComparer<T>.Default.Equals(actual, expected), $"Expected {expected}; received {actual}");
}

internal sealed record CaseResult(string Protocol, string Name, bool Passed, string? Error);
