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

        foreach (byte b in data)
        {
            char c = (char)b;

            if (_escaped)
            {
                _escaped = false;
                Append(c, ref overflows);
                continue;
            }

            switch (c)
            {
                case '\\':
                    _escaped = true;
                    Append(c, ref overflows);
                    break;

                case ';':
                    if (_discarding)
                    {
                        _discarding = false;
                        overflows++;
                    }
                    else
                    {
                        string statement = _pending.ToString().Trim();
                        if (statement.Length > 0) output.Add(statement);
                    }
                    _pending.Clear();
                    break;

                default:
                    Append(c, ref overflows);
                    break;
            }
        }

        return overflows;
    }

    private void Append(char c, ref int overflows)
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
