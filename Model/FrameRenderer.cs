using System;

namespace SpriteEditXL.Model;

/// <summary>
/// Pure-C# per-pixel RGB rasterizer for a frame at arbitrary integer scale.
/// Used by both the Avalonia editor and the GIF exporter.
/// </summary>
public static class FrameRenderer
{
    public readonly record struct GridSize(int Cols, int Rows);

    public static GridSize GetGridSize(Workspace w, Options o)
    {
        int cols, rows;
        if (o.Freestyle)
        {
            int Max(int a, int b, int c, int d) => Math.Max(Math.Max(a, b), Math.Max(c, d));
            int pMax = Max(
                o.P0Offset >= 0 ? o.P0Offset + 8 : 0,
                o.P1Offset >= 0 ? o.P1Offset + 8 : 0,
                o.P2Offset >= 0 ? o.P2Offset + 8 : 0,
                o.P3Offset >= 0 ? o.P3Offset + 8 : 0);
            int mMax = Max(
                o.M0Offset >= 0 ? o.M0Offset + 2 : 0,
                o.M1Offset >= 0 ? o.M1Offset + 2 : 0,
                o.M2Offset >= 0 ? o.M2Offset + 2 : 0,
                o.M3Offset >= 0 ? o.M3Offset + 2 : 0);
            cols = Math.Max(Math.Max(pMax, mMax), 8);
        }
        else
        {
            int cols01 = Defines.SpriteWidthPerMode[(int)o.MergeMode] + o.SpriteGap01;
            int cols23 = Defines.SpriteWidthPerMode[(int)o.MergeMode] + o.SpriteGap23;
            cols = o.MergeMode.IsPlayer23Mode() ? Math.Max(cols01, cols23 + o.PairGap) : cols01;
        }
        cols += o.BackOffsetH * 2;
        rows = o.SpriteHeight + o.BackOffsetV * 2;
        return new GridSize(cols, rows);
    }

    /// <summary>Render a frame's pixels into a BGRA byte buffer at the given cell size.</summary>
    public static byte[] RenderBgra(Workspace w, Options o, EditorSession s, int frameIdx, int cellW, int cellH, out int width, out int height)
    {
        var size = GetGridSize(w, o);
        int cols = size.Cols;
        int rows = size.Rows;
        width = cols * cellW;
        height = rows * cellH;
        var buf = new byte[width * height * 4];
        var engine = new PixelEngine(w, o);

        bool refActive = o.ShowReference && w.ReferenceImage != null && w.ReferenceImageWidth > 0;

        for (int r = 0; r < rows; r++)
        {
            int dataRow = r - o.BackOffsetV;
            var colors = engine.GetColors(frameIdx, dataRow);
            for (int c = 0; c < cols; c++)
            {
                int dataCol = c - o.BackOffsetH;
                int ci = 0;
                byte cval;
                bool transparent = true;
                if (dataRow >= 0 && dataRow < o.SpriteHeight && dataCol >= 0 && dataCol < engine.ColumnsActive)
                {
                    ci = engine.GetColorOn(frameIdx, dataCol, dataRow, s.Layer01Visible, s.Layer23Visible);
                    cval = colors[ci];
                    transparent = (ci == 0);

                    if (ci == 0 && o.ShowBackground && w.BackgroundImage != null)
                    {
                        cval = SampleBackground(w, o, c, r);
                        transparent = false;
                    }
                }
                else cval = w.BackgroundColor;

                // Reference-image overlay: shows through transparent cells.
                if (refActive && transparent && TrySampleReference(w, o, c, r, out Rgb refRgb))
                {
                    FillCell(buf, width, c * cellW, r * cellH, cellW, cellH, refRgb);
                    continue;
                }

                var rgb = AtariPalette.ToRgb(cval, o.Palette);
                FillCell(buf, width, c * cellW, r * cellH, cellW, cellH, rgb);
            }
        }
        return buf;
    }

    /// <summary>
    /// Sample the reference image at the Atari-cell (col,row) position, taking the pan offset
    /// and per-cell scale factor into account. Returns false if the sample is outside the image.
    /// </summary>
    private static bool TrySampleReference(Workspace w, Options o, int col, int row, out Rgb rgb)
    {
        rgb = default;
        if (w.ReferenceImage == null) return false;
        int sx = o.ReferenceSourceX + col * Math.Max(1, o.ReferenceScaleX);
        int sy = o.ReferenceSourceY + row * Math.Max(1, o.ReferenceScaleY);
        if (sx < 0 || sy < 0 || sx >= w.ReferenceImageWidth || sy >= w.ReferenceImageHeight) return false;
        int ofs = (sy * w.ReferenceImageWidth + sx) * 4;
        // BGRA in the buffer.
        rgb = new Rgb(w.ReferenceImage[ofs + 2], w.ReferenceImage[ofs + 1], w.ReferenceImage[ofs + 0]);
        return true;
    }

    private static byte SampleBackground(Workspace w, Options o, int col, int row)
    {
        if (w.BackgroundImage == null) return w.BackgroundColor;
        int backWidth = 4 * (o.BackImageWidth > 0 ? o.BackImageWidth : (int)Math.Ceiling(GetGridSize(w, o).Cols / 4.0));
        int ofs = o.BackOffset * 4 + row * backWidth + col;
        if (ofs < 0 || ofs >= w.BackgroundImage.Length) return 0;
        return w.BackgroundImage[ofs];
    }

    private static void FillCell(byte[] buf, int stride, int x0, int y0, int w, int h, Rgb c)
    {
        for (int yy = 0; yy < h; yy++)
        {
            int row = (y0 + yy) * stride * 4 + x0 * 4;
            for (int xx = 0; xx < w; xx++)
            {
                buf[row++] = c.B;
                buf[row++] = c.G;
                buf[row++] = c.R;
                buf[row++] = 255;
            }
        }
    }
}
