namespace NekoPcbEmulator.Core;

/// <summary>
/// A straight (non-premultiplied) RGBA color. The wire representation used by both
/// protocols is a 32 bit integer laid out as <c>0xRRGGBBAA</c>.
/// </summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A)
{
    public static readonly Rgba Off = default;

    public bool IsOff => A == 0;

    /// <summary>The color packed as <c>0xRRGGBBAA</c>.</summary>
    public uint Packed => ((uint)R << 24) | ((uint)G << 16) | ((uint)B << 8) | A;

    public static Rgba FromPacked(uint value) =>
        new((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);

    /// <summary>
    /// Composites <paramref name="src"/> over <paramref name="dst"/> using the standard
    /// source-over operator. Used by the pixel panel so that alpha behaves the way a
    /// drawing API is expected to behave.
    /// </summary>
    public static Rgba Over(Rgba dst, Rgba src)
    {
        if (src.A == 255 || dst.A == 0) return src;
        if (src.A == 0) return dst;

        // Everything is scaled by 255 to stay in integer math.
        int sa = src.A;
        int da = dst.A * (255 - sa) / 255;
        int oa = sa + da;
        if (oa == 0) return Off;

        byte Mix(byte s, byte d) => (byte)((s * sa + d * da) / oa);
        return new Rgba(Mix(src.R, dst.R), Mix(src.G, dst.G), Mix(src.B, dst.B), (byte)oa);
    }

    /// <summary>
    /// Flattens the color against black and premultiplies it, which is how an emissive
    /// LED behaves: alpha is the emitted intensity. Returns 0xAARRGGBB (BGRA in memory),
    /// directly consumable as a GDI+ <c>Format32bppPArgb</c> pixel.
    /// </summary>
    public uint ToOpaquePremultipliedBgra()
    {
        uint r = (uint)(R * A / 255);
        uint g = (uint)(G * A / 255);
        uint b = (uint)(B * A / 255);
        return 0xFF000000u | (r << 16) | (g << 8) | b;
    }

    public override string ToString() => $"#{Packed:X8}";
}
