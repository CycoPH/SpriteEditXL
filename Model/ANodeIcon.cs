using System;
using System.Text;

namespace SpriteEditXL.Model;

/// <summary>
/// aNode icon import/export (126-character hex string).
/// Ported from parseAnodeIcon() in io.js.
/// Layout: 20 bytes P0, 20 bytes P1, 20 bytes combined missile (m0 low 2 bits, m1 bits 2-3), c0, c1, isntsc.
/// </summary>
public static class ANodeIcon
{
    public const int HexLength = 126;

    public sealed class Data
    {
        public byte[] P0 = new byte[20];
        public byte[] P1 = new byte[20];
        public byte[] M0 = new byte[20];
        public byte[] M1 = new byte[20];
        public byte C0, C1;
        public bool IsNtsc;
    }

    public static Data? Parse(string hex)
    {
        if (hex is null || hex.Length != HexLength) return null;
        var bytes = new byte[63];
        for (int i = 0; i < 63; i++)
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                return null;

        var d = new Data();
        Array.Copy(bytes, 0, d.P0, 0, 20);
        Array.Copy(bytes, 20, d.P1, 0, 20);
        for (int i = 0; i < 20; i++)
        {
            d.M0[i] = (byte)(bytes[40 + i] & 3);
            d.M1[i] = (byte)((bytes[40 + i] >> 2) & 3);
        }
        d.C0 = bytes[60];
        d.C1 = bytes[61];
        d.IsNtsc = bytes[62] != 0;
        return d;
    }

    public static string Encode(Frame frame, PaletteKind palette)
    {
        var sb = new StringBuilder(HexLength);
        for (int i = 0; i < 20; i++) sb.Append(frame.Player[0][i].ToString("x2"));
        for (int i = 0; i < 20; i++) sb.Append(frame.Player[1][i].ToString("x2"));
        for (int i = 0; i < 20; i++)
        {
            byte m = (byte)((frame.Missile[0][i] & 3) | ((frame.Missile[1][i] & 3) << 2));
            sb.Append(m.ToString("x2"));
        }
        sb.Append(frame.Colors[0].ToString("x2"));
        sb.Append(frame.Colors[1].ToString("x2"));
        sb.Append((palette == PaletteKind.NTSC ? (byte)1 : (byte)0).ToString("x2"));
        return sb.ToString();
    }

    /// <summary>Returns whether current options describe a valid aNode icon layout.</summary>
    public static bool IsAnodeMode(Options o) =>
        o.LineResolution == 2 && o.MergeMode == SpriteMode.PM0PM1 &&
        o.PairGap == 0 && o.SpriteGap01 == 0 && o.SpriteHeight == 20;
}
