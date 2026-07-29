using System.Globalization;
using System.Text;

namespace NekoPcbEmulator.Core.Devices.PcbA;

/// <summary>
/// Lexical helpers for the raw ASCII protocol.
///
/// A backslash escapes the following character everywhere, which is what lets a payload
/// string contain the statement terminator or a closing bracket:
/// <c>\; \&lt; \&gt; \\ \n \r \t</c>.
/// </summary>
public static class AsciiSyntax
{
    /// <summary>Separators accepted between arguments. Runs of them collapse.</summary>
    private static bool IsSeparator(char c) => c is ',' or ' ' or '\t' or '\r' or '\n';

    public static string Unescape(string text)
    {
        if (!text.Contains('\\')) return text;

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i == text.Length - 1)
            {
                sb.Append(text[i]);
                continue;
            }

            char next = text[++i];
            sb.Append(next switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '0' => '\0',
                _ => next,
            });
        }
        return sb.ToString();
    }

    /// <summary>
    /// Finds the argument block delimited by <paramref name="open"/>/<paramref name="close"/>,
    /// honouring escapes and nesting. Returns the raw (still escaped) inner text.
    /// </summary>
    public static bool TryExtractBlock(string text, int startIndex, char open, char close, out string inner, out int afterIndex)
    {
        inner = "";
        afterIndex = startIndex;

        int i = startIndex;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        if (i >= text.Length || text[i] != open) return false;

        int depth = 0;
        int contentStart = i + 1;
        for (; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\') { i++; continue; }
            if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                {
                    inner = text[contentStart..i];
                    afterIndex = i + 1;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Splits an argument block into at most <paramref name="maxParts"/> parts. The final part
    /// is the untouched remainder, so a trailing string argument may contain separators.
    /// </summary>
    public static List<string> SplitArgs(string inner, int maxParts)
    {
        var parts = new List<string>();
        int i = 0;

        while (parts.Count < maxParts - 1)
        {
            while (i < inner.Length && IsSeparator(inner[i])) i++;
            if (i >= inner.Length) return parts;

            int tokenStart = i;
            while (i < inner.Length && !IsSeparator(inner[i]))
            {
                if (inner[i] == '\\') i++;
                i++;
            }
            parts.Add(inner[tokenStart..Math.Min(i, inner.Length)]);
        }

        while (i < inner.Length && IsSeparator(inner[i])) i++;
        if (i < inner.Length) parts.Add(inner[i..].TrimEnd());
        return parts;
    }

    /// <summary>Splits into every whitespace/comma separated token.</summary>
    public static List<string> SplitAll(string inner) => SplitArgs(inner, int.MaxValue);

    /// <summary>Accepts decimal, <c>0x</c>-prefixed hex and <c>#</c>-prefixed hex.</summary>
    public static bool TryParseInt(string token, out long value)
    {
        value = 0;
        token = token.Trim();
        if (token.Length == 0) return false;

        bool negative = false;
        if (token[0] is '+' or '-')
        {
            negative = token[0] == '-';
            token = token[1..];
        }

        bool ok;
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            ok = ulong.TryParse(token[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hex) && Assign(hex, out value);
        else if (token.StartsWith('#'))
            ok = ulong.TryParse(token[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hash) && Assign(hash, out value);
        else
            ok = long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        if (ok && negative) value = -value;
        return ok;

        static bool Assign(ulong source, out long target)
        {
            target = unchecked((long)source);
            return true;
        }
    }

    public static bool TryParseInt32(string token, out int value)
    {
        value = 0;
        if (!TryParseInt(token, out long wide) || wide is < int.MinValue or > int.MaxValue) return false;
        value = (int)wide;
        return true;
    }

    /// <summary>Parses an RGBA literal: <c>0xRRGGBBAA</c>, <c>#RRGGBBAA</c> or plain decimal.</summary>
    public static bool TryParseRgba(string token, out Rgba color)
    {
        color = Rgba.Off;
        if (!TryParseInt(token, out long value) || value is < 0 or > uint.MaxValue) return false;
        color = Rgba.FromPacked((uint)value);
        return true;
    }

    public static bool TryParseBool(string token, out bool value)
    {
        value = false;
        switch (token.Trim().ToUpperInvariant())
        {
            case "1" or "TRUE" or "ON" or "HIGH" or "YES":
                value = true;
                return true;
            case "0" or "FALSE" or "OFF" or "LOW" or "NO":
                value = false;
                return true;
            default:
                return false;
        }
    }
}
