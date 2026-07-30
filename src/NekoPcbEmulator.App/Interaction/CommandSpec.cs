using System.Drawing;

namespace NekoPcbEmulator.App.Interaction;

public enum FieldKind
{
    Integer,
    Rgba,
    Boolean,
    Text,
}

/// <summary>One editable parameter of a command, as presented in the send dialog.</summary>
public sealed record CommandField(
    string Name,
    FieldKind Kind,
    string Default = "",
    int Minimum = 0,
    int Maximum = 255,
    string? Hint = null);

/// <summary>
/// A command a peripheral accepts.
///
/// <see cref="Encode"/> produces the exact bytes that go on the wire, and <see cref="Preview"/>
/// the human-readable form shown in the dialog. Both are derived from the entered values, so
/// what the dialog shows is what is actually sent — there is no second code path that could
/// drift from the protocol.
/// </summary>
public sealed record CommandSpec(
    string Name,
    string Summary,
    IReadOnlyList<CommandField> Fields,
    Func<IReadOnlyList<string>, byte[]> Encode,
    Func<IReadOnlyList<string>, string> Preview);

/// <summary>
/// A clickable region of the board, in design-space coordinates, together with the commands
/// its peripheral understands.
/// </summary>
public sealed record Hotspot(
    string Title,
    RectangleF Bounds,
    IReadOnlyList<CommandSpec> Commands,
    float CornerRadius = 6f);
