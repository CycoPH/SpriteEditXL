using System;

namespace SpriteEditXL.Model;

public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>
/// Converts Atari color bytes (upper nibble = hue, lower = luma) to RGB,
/// matching the YIQ synthesis in getByteRGB() from app.js.
/// </summary>
public static class AtariPalette
{
    private static readonly Rgb[] PalCache  = Build(PaletteKind.PAL);
    private static readonly Rgb[] NtscCache = Build(PaletteKind.NTSC);

    public static Rgb ToRgb(byte cval, PaletteKind palette)
        => (palette == PaletteKind.PAL ? PalCache : NtscCache)[cval];

    private static Rgb[] Build(PaletteKind palette)
    {
        var table = new Rgb[256];
        for (int cval = 0; cval < 256; cval++)
            table[cval] = Synthesize((byte)cval, palette);
        return table;
    }

    private static Rgb Synthesize(byte cval, PaletteKind palette)
    {
        int cr = (cval >> 4) & 15;
        int lm = cval & 15;
        double crlv = cr != 0 ? 50.0 : 0.0;

        double phase = (palette == PaletteKind.PAL)
            ? ((cr - 1) * 25.7 - 15) * (2 * Math.PI / 360)
            : ((cr - 1) * 25.0 - 58) * (2 * Math.PI / 360);

        double y = 255.0 * (lm + 1) / 16.0;
        double i = crlv * Math.Cos(phase);
        double q = crlv * Math.Sin(phase);

        int r = Defines.Clamp((int)Math.Round(y + 0.956 * i + 0.621 * q), 0, 255);
        int g = Defines.Clamp((int)Math.Round(y - 0.272 * i - 0.647 * q), 0, 255);
        int b = Defines.Clamp((int)Math.Round(y - 1.107 * i + 1.704 * q), 0, 255);
        return new Rgb((byte)r, (byte)g, (byte)b);
    }
}
