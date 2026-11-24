namespace SpriteEditXL.Model;

public static class Defines
{
    public const string Version = "1.0.0";
    public const string ProductName = "SpriteEditXL";
    public const int MaxFileSize = 10 * 1024;
    public const int DefaultDelayTime = 10;

    public static readonly byte[] AplHeader  = { 0x9A, 0xF8, 0x39, 0x21 };
    public static readonly byte[] Apl2Header = { 0x45, 0x64, 0x52, 0x32 };
    public static readonly byte[] SprHeader  = { 0x53, 0x70, 0x72, 0x21 };

    /// <summary>Column count for each sprite mode (0..7 index).</summary>
    public static readonly int[] SpriteWidthPerMode = { 8, 10, 10, 10, 8, 10, 10, 10 };

    /// <summary>Pixel-cell sizes available on the zoom slider.</summary>
    public static readonly int[] ZoomCellSize =
    {
        4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 36, 40, 45, 50, 55, 60, 70, 80, 90, 100
    };

    public static readonly string[] DliMap = { "back", "c0", "c1", "c2", "c3" };
    public static readonly string?[] DliColorMap = { "back", "c0", "c1", null, null, "c2", "c3" };

    /// <summary>GTIA-ordered bit reverse table (reversedBytes in defines.js).</summary>
    public static readonly byte[] ReversedBytes = BuildReversedBytes();
    public static readonly byte[] Reversed2Bits = { 0x00, 0x02, 0x01, 0x03 };

    private static byte[] BuildReversedBytes()
    {
        var table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            int b = i;
            b = ((b & 0xF0) >> 4) | ((b & 0x0F) << 4);
            b = ((b & 0xCC) >> 2) | ((b & 0x33) << 2);
            b = ((b & 0xAA) >> 1) | ((b & 0x55) << 1);
            table[i] = (byte)b;
        }
        return table;
    }

    public static int Clamp(int n, int min, int max) => n < min ? min : (n > max ? max : n);
}
