using System;

namespace SpriteEditXL.Model;

/// <summary>
/// Per-row color tables for DLI (display-list interrupt) palette changes.
/// Each array is one entry per sprite row.
/// </summary>
public sealed class DliTable
{
    public byte[] Back { get; set; } = Array.Empty<byte>();
    public byte[] C0 { get; set; } = Array.Empty<byte>();
    public byte[] C1 { get; set; } = Array.Empty<byte>();
    public byte[] C2 { get; set; } = Array.Empty<byte>();
    public byte[] C3 { get; set; } = Array.Empty<byte>();

    public static DliTable Create(int height, byte[] baseColors, byte backColor)
    {
        return new DliTable
        {
            Back = Fill(height, backColor),
            C0 = Fill(height, baseColors[0]),
            C1 = Fill(height, baseColors[1]),
            C2 = Fill(height, baseColors[2]),
            C3 = Fill(height, baseColors[3])
        };
    }

    private static byte[] Fill(int n, byte v)
    {
        var a = new byte[n];
        for (int i = 0; i < n; i++) a[i] = v;
        return a;
    }

    public DliTable Clone() => new()
    {
        Back = (byte[])Back.Clone(),
        C0 = (byte[])C0.Clone(),
        C1 = (byte[])C1.Clone(),
        C2 = (byte[])C2.Clone(),
        C3 = (byte[])C3.Clone()
    };
}

/// <summary>Single animation frame: four players, four missiles, four colors, a DLI table.</summary>
public sealed class Frame
{
    /// <summary>player[p][row] — bit7 is leftmost pixel.</summary>
    public byte[][] Player { get; set; } = new byte[4][];
    /// <summary>missile[p][row] — only two low bits are used.</summary>
    public byte[][] Missile { get; set; } = new byte[4][];
    /// <summary>Base color registers (COLPM0..3).</summary>
    public byte[] Colors { get; set; } = new byte[] { 0x24, 0xC8, 0x86, 0xEA };
    public int DelayTime { get; set; } = Defines.DefaultDelayTime;
    public DliTable Dli { get; set; } = new();

    public static Frame CreateEmpty(int height, byte backgroundColor)
    {
        var f = new Frame();
        for (int p = 0; p < 4; p++)
        {
            f.Player[p] = new byte[height];
            f.Missile[p] = new byte[height];
        }
        f.Dli = DliTable.Create(height, f.Colors, backgroundColor);
        return f;
    }

    public Frame Clone()
    {
        var f = new Frame
        {
            Colors = (byte[])Colors.Clone(),
            DelayTime = DelayTime,
            Dli = Dli.Clone()
        };
        for (int p = 0; p < 4; p++)
        {
            f.Player[p] = (byte[])Player[p].Clone();
            f.Missile[p] = (byte[])Missile[p].Clone();
        }
        return f;
    }

    /// <summary>Resize per-row arrays to <paramref name="height"/>, preserving existing rows.</summary>
    public void SetHeight(int height)
    {
        if (Player == null || Player.Length != 4) Player = new byte[4][];
        if (Missile == null || Missile.Length != 4) Missile = new byte[4][];
        for (int p = 0; p < 4; p++)
        {
            Player[p] = Resize(Player[p], height);
            Missile[p] = Resize(Missile[p], height);
        }
        Dli ??= new DliTable();
        Dli.Back = Resize(Dli.Back, height);
        Dli.C0 = Resize(Dli.C0, height);
        Dli.C1 = Resize(Dli.C1, height);
        Dli.C2 = Resize(Dli.C2, height);
        Dli.C3 = Resize(Dli.C3, height);
    }

    private static byte[] Resize(byte[]? src, int len)
    {
        var dst = new byte[len];
        if (src != null) Array.Copy(src, 0, dst, 0, Math.Min(src.Length, len));
        return dst;
    }
}
