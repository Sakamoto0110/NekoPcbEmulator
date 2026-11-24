using System.Text;

namespace NekoPcbEmulator.Core.Devices.PcbA;

/// <summary>
/// Per-connection reassembly for the ASCII protocol. Bytes arrive in arbitrary chunks; this
/// splits them into <c>;</c>-terminated statements, treating an escaped <c>\;</c> as literal
/// text so a payload string can contain a semicolon.
/// </summary>
public sealed class AsciiStatementReader
{
    private readonly StringBuilder _pending = new();
    private readonly int _maxStatementLength;
    private bool _escaped;
    private bool _discarding;

    public AsciiStatementReader(int maxStatementLength = 4096) => _maxStatementLength = maxStatementLength;

    /// <summary>
    /// Appends every complete statement (terminator stripped) to <paramref name="output"/>.
    /// Returns how many statements were dropped for exceeding the length limit; the caller is
    /// expected to report those as protocol errors.
    /// </summary>
    public int Push(ReadOnlySpan<byte> data, List<string> output)
    {
        int overflows = 0;
        Push(data, statement =>
        {
            if (statement is null) overflows++;
            else output.Add(statement);
        });
        return overflows;
    }

    /// <summary>
    /// Emits statements in wire order. A null statement marks an overlong statement, so
    /// callers can keep error replies in order with replies to valid commands.
    /// </summary>
    public void Push(ReadOnlySpan<byte> data, Action<string?> onStatement)
    {
        foreach (byte b in data)
        {
            char c = (char)b;

            if (_escaped)
            {
                _escaped = false;
                Append(c);
                continue;
            }

            switch (c)
            {
                case '\\':
                    _escaped = true;
                    Append(c);
                    break;

                case ';':
                    string? statement = _discarding ? null : _pending.ToString().Trim();
                    _discarding = false;
                    _pending.Clear();
                    if (statement is null || statement.Length > 0) onStatement(statement);
                    break;

                default:
                    Append(c);
                    break;
            }
        }
    }

    private void Append(char c)
    {
        if (_discarding) return;

        // Ignore leading whitespace so line-oriented clients can pretty-print their commands.
        if (_pending.Length == 0 && char.IsWhiteSpace(c)) return;

        if (_pending.Length >= _maxStatementLength)
        {
            _pending.Clear();
            _discarding = true;
            _escaped = false;
            return;
        }

        _pending.Append(c);
    }

    public void Reset()
    {
        _pending.Clear();
        _escaped = false;
        _discarding = false;
    }
}
