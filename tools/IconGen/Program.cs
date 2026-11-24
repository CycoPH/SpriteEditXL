using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

// Generates SpriteEditXL application icon:
//  - a chunky pixel-art sprite on a rounded brown badge (matches app brown #462F21)
//  - a bright "XL" tag in the corner
//
// Outputs:
//   Assets/AppIcon.png       (256x256 master)
//   Assets/AppIcon-16.png, -32, -48, -64, -128, -256
//   Assets/AppIcon.ico       (multi-size ICO for Windows exe)

static class Program
{
    // 16x16 pixel-art sprite — a stylized little space alien in the Atari palette.
    // '.' = transparent, each other char is a palette index below.
    static readonly string[] Sprite = new[]
    {
        "....GGGGGGGG....",
        "...GwwwwwwwwG...",
        "..GwwBBwwBBwwG..",
        "..GwwBBwwBBwwG..",
        ".GwwwwwwwwwwwwG.",
        ".GwRRwwRRwwRRwG.",
        ".GwwwwwwwwwwwwG.",
        "..GGwwwwwwwwGG..",
        "...GGwwwwwwGG...",
        "....GGwwwwGG....",
        ".....Gwwww G....",
        "....G.GwwG.G....",
        "...G...GG...G...",
        "..G..........G..",
        ".G............G.",
        "G..............G",
    };

    static readonly Dictionary<char, SKColor> Palette = new()
    {
        { 'G', new SKColor(0x6B, 0xD9, 0x5B) },   // alien green body
        { 'w', new SKColor(0xB7, 0xF4, 0xA6) },   // lighter green highlight
        { 'B', new SKColor(0x12, 0x12, 0x12) },   // eyes
        { 'R', new SKColor(0xE4, 0x6A, 0x3A) },   // belt / mouth
    };

    static readonly SKColor BgTop     = new SKColor(0x6E, 0x4A, 0x33); // lighter brown top
    static readonly SKColor BgBottom  = new SKColor(0x3A, 0x26, 0x1A); // darker brown bottom
    static readonly SKColor BgBorder  = new SKColor(0x1E, 0x14, 0x0E);
    static readonly SKColor XlFill    = new SKColor(0xF4, 0xD4, 0x7A); // app gold
    static readonly SKColor XlOutline = new SKColor(0x2A, 0x1D, 0x14);

    static void Main()
    {
        string assetsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Assets"));
        Directory.CreateDirectory(assetsDir);

        int[] sizes = { 16, 32, 48, 64, 128, 256 };
        var pngs = new Dictionary<int, byte[]>();
        foreach (int s in sizes)
        {
            using var surf = SKSurface.Create(new SKImageInfo(s, s, SKColorType.Bgra8888, SKAlphaType.Premul));
            Draw(surf.Canvas, s);
            using var img = surf.Snapshot();
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            byte[] bytes = data.ToArray();
            pngs[s] = bytes;
            File.WriteAllBytes(Path.Combine(assetsDir, s == 256 ? "AppIcon.png" : $"AppIcon-{s}.png"), bytes);
            Console.WriteLine($"wrote AppIcon-{s}.png ({bytes.Length} bytes)");
        }

        // 256 master duplicate of the detailed version
        File.WriteAllBytes(Path.Combine(assetsDir, "AppIcon.png"), pngs[256]);

        // Build ICO
        string icoPath = Path.Combine(assetsDir, "AppIcon.ico");
        WriteIco(icoPath, new[] { 16, 32, 48, 64, 128, 256 }, pngs);
        Console.WriteLine($"wrote {icoPath}");
    }

    static void Draw(SKCanvas c, int size)
    {
        c.Clear(SKColors.Transparent);

        // Rounded badge with vertical gradient.
        float r = size * 0.18f;
        var rect = new SKRect(0.5f, 0.5f, size - 0.5f, size - 0.5f);
        using (var bg = new SKPaint { IsAntialias = true })
        {
            bg.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, size),
                new[] { BgTop, BgBottom }, null, SKShaderTileMode.Clamp);
            c.DrawRoundRect(rect, r, r, bg);
        }
        using (var stroke = new SKPaint {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1f, size * 0.025f), Color = BgBorder
        })
        {
            c.DrawRoundRect(rect, r, r, stroke);
        }

        // --- sprite grid, centered, slightly offset up so "XL" badge can sit lower-right.
        const int grid = 16;
        float cellSize = size / 18f;
        float spriteW = grid * cellSize;
        float ox = (size - spriteW) / 2f;
        float oy = (size - spriteW) / 2f - size * 0.03f;

        using var pixelPaint = new SKPaint { IsAntialias = false };
        for (int y = 0; y < grid; y++)
        {
            string row = Sprite[y];
            for (int x = 0; x < grid; x++)
            {
                char ch = row[x];
                if (!Palette.TryGetValue(ch, out var col)) continue;
                pixelPaint.Color = col;
                var cell = new SKRect(
                    ox + x * cellSize,
                    oy + y * cellSize,
                    ox + (x + 1) * cellSize,
                    oy + (y + 1) * cellSize);
                c.DrawRect(cell, pixelPaint);
            }
        }

        // --- "XL" badge, lower-right corner.
        float badgeW = size * 0.42f;
        float badgeH = size * 0.26f;
        float bx = size - badgeW - size * 0.06f;
        float by = size - badgeH - size * 0.06f;
        var badgeRect = new SKRect(bx, by, bx + badgeW, by + badgeH);
        float badgeR = badgeH * 0.35f;

        // Drop shadow
        using (var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 120) })
        {
            shadow.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, size * 0.015f);
            var shr = new SKRect(badgeRect.Left + size * 0.01f, badgeRect.Top + size * 0.012f,
                                 badgeRect.Right + size * 0.01f, badgeRect.Bottom + size * 0.012f);
            c.DrawRoundRect(shr, badgeR, badgeR, shadow);
        }

        using (var badge = new SKPaint { IsAntialias = true, Color = XlFill })
            c.DrawRoundRect(badgeRect, badgeR, badgeR, badge);
        using (var badgeStroke = new SKPaint {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1f, size * 0.012f), Color = XlOutline
        })
            c.DrawRoundRect(badgeRect, badgeR, badgeR, badgeStroke);

        // "XL" text, bold, centered in badge.
        using var text = new SKPaint {
            IsAntialias = true, Color = XlOutline,
            Typeface = SKTypeface.FromFamilyName(
                "Arial", SKFontStyleWeight.ExtraBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
            TextSize = badgeH * 0.82f,
            TextAlign = SKTextAlign.Center,
            SubpixelText = true
        };
        var metrics = text.FontMetrics;
        float cx = badgeRect.MidX;
        float cy = badgeRect.MidY - (metrics.Ascent + metrics.Descent) / 2f;
        c.DrawText("XL", cx, cy, text);
    }

    static void WriteIco(string path, int[] sizes, Dictionary<int, byte[]> pngs)
    {
        // ICO directory header (6 bytes) + per-image entry (16 bytes each), then PNG data concatenated.
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0);          // reserved
        bw.Write((ushort)1);          // type: icon
        bw.Write((ushort)sizes.Length);

        int dataOffset = 6 + 16 * sizes.Length;
        foreach (int s in sizes)
        {
            byte[] png = pngs[s];
            bw.Write((byte)(s == 256 ? 0 : s)); // width (0 == 256)
            bw.Write((byte)(s == 256 ? 0 : s)); // height
            bw.Write((byte)0);                  // palette colors
            bw.Write((byte)0);                  // reserved
            bw.Write((ushort)1);                // color planes
            bw.Write((ushort)32);               // bpp
            bw.Write((uint)png.Length);         // bytes in resource
            bw.Write((uint)dataOffset);
            dataOffset += png.Length;
        }

        foreach (int s in sizes)
        {
            byte[] png = pngs[s];
            bw.Write(png, 0, png.Length);
        }
    }
}
