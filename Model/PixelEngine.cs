using System;

namespace SpriteEditXL.Model;

/// <summary>
/// Pixel read/write and mask generation. Ported from app.js: getMasks, getColorOn, setColorOn, getColors.
/// Color indices: 0 = background, 1=C0, 2=C1, 3=C0|C1 (OR), 5=C2, 6=C3, 7=C2|C3.
/// </summary>
public sealed class PixelEngine
{
    private readonly Workspace _ws;
    private readonly Options _opt;

    public PixelEngine(Workspace ws, Options opt)
    {
        _ws = ws;
        _opt = opt;
    }

    /// <summary>
    /// Effective column count including sprite gaps (cols01 vs cols23 + pairGap in P23 mode).
    /// Matches editorWindow.columnsActive in app.js and FrameRenderer.GetGridSize.
    /// </summary>
    public int ColumnsActive
    {
        get
        {
            if (_opt.Freestyle)
            {
                int Max(int a, int b, int c, int d) => Math.Max(Math.Max(a, b), Math.Max(c, d));
                int pMax = Max(
                    _opt.P0Offset >= 0 ? _opt.P0Offset + 8 : 0,
                    _opt.P1Offset >= 0 ? _opt.P1Offset + 8 : 0,
                    _opt.P2Offset >= 0 ? _opt.P2Offset + 8 : 0,
                    _opt.P3Offset >= 0 ? _opt.P3Offset + 8 : 0);
                int mMax = Max(
                    _opt.M0Offset >= 0 ? _opt.M0Offset + 2 : 0,
                    _opt.M1Offset >= 0 ? _opt.M1Offset + 2 : 0,
                    _opt.M2Offset >= 0 ? _opt.M2Offset + 2 : 0,
                    _opt.M3Offset >= 0 ? _opt.M3Offset + 2 : 0);
                return Math.Max(Math.Max(pMax, mMax), 8);
            }
            int basew = Defines.SpriteWidthPerMode[(int)_opt.MergeMode];
            int cols01 = basew + _opt.SpriteGap01;
            int cols23 = basew + _opt.SpriteGap23;
            return _opt.MergeMode.IsPlayer23Mode() ? Math.Max(cols01, cols23 + _opt.PairGap) : cols01;
        }
    }

    /// <summary>
    /// For a given pixel column, return the eight per-sprite bit masks
    /// [mp0, mp1, mm0, mm1, mp2, mp3, mm2, mm3]. A non-zero mask means this
    /// column lies inside that sprite element.
    /// </summary>
    public int[] GetMasks(int col)
    {
        int mp0, mp1, mm0, mm1, mp2, mp3, mm2, mm3;

        if (_opt.Freestyle)
        {
            mp0 = PMask(col, _opt.P0Offset);
            mp1 = PMask(col, _opt.P1Offset);
            mp2 = PMask(col, _opt.P2Offset);
            mp3 = PMask(col, _opt.P3Offset);
            mm0 = MMask(col, _opt.M0Offset);
            mm1 = MMask(col, _opt.M1Offset);
            mm2 = MMask(col, _opt.M2Offset);
            mm3 = MMask(col, _opt.M3Offset);
        }
        else
        {
            mp0 = col > 8 ? 0 : (0x80 >> col);
            int o = col - _opt.SpriteGap01;
            mp1 = o < 0 ? 0 : (0x80 >> o);
            o = col - 8;
            mm0 = o < 0 ? 0 : (0x02 >> o);
            o = col - 8 - _opt.SpriteGap01;
            mm1 = o < 0 ? 0 : (0x02 >> o);

            o = col - _opt.PairGap;
            mp2 = o < 0 ? 0 : (0x80 >> o);
            o = col - _opt.PairGap - _opt.SpriteGap23;
            mp3 = o < 0 ? 0 : (0x80 >> o);
            o = col - _opt.PairGap - 8;
            mm2 = o < 0 ? 0 : (0x02 >> o);
            o = col - _opt.PairGap - 8 - _opt.SpriteGap23;
            mm3 = o < 0 ? 0 : (0x02 >> o);

            bool missile = _opt.MergeMode.IsMissileMode();
            bool p23 = _opt.MergeMode.IsPlayer23Mode();

            if (!missile) { mm0 = 0; mm1 = 0; mm2 = 0; mm3 = 0; }
            if (!p23)     { mp2 = 0; mp3 = 0; mm2 = 0; mm3 = 0; }
        }

        if (_ws.HiddenSprites[0]) { mp0 = 0; mm0 = 0; }
        if (_ws.HiddenSprites[1]) { mp1 = 0; mm1 = 0; }
        if (_ws.HiddenSprites[2]) { mp2 = 0; mm2 = 0; }
        if (_ws.HiddenSprites[3]) { mp3 = 0; mm3 = 0; }

        return new[] { mp0, mp1, mm0, mm1, mp2, mp3, mm2, mm3 };

        static int PMask(int col, int off) =>
            off < 0 || (col - off) < 0 || (col - off) > 7 ? 0 : (0x80 >> (col - off));
        static int MMask(int col, int off) =>
            off < 0 || (col - off) < 0 || (col - off) > 1 ? 0 : (0x02 >> (col - off));
    }

    /// <summary>Effective 8-entry color table for the frame/row (respects CommonPalette and DLI).</summary>
    public byte[] GetColors(int frame, int row)
    {
        if (_opt.CommonPalette) frame = 0;
        if (frame < 0 || frame >= _ws.Frames.Count) frame = 0;
        var f = _ws.Frames[frame];
        byte c0 = f.Colors[0], c1 = f.Colors[1], c2 = f.Colors[2], c3 = f.Colors[3];

        byte back = _ws.BackgroundColor;
        if (_opt.DliOn && row >= 0 && row < _opt.SpriteHeight)
        {
            back = f.Dli.Back[row];
            c0 = f.Dli.C0[row]; c1 = f.Dli.C1[row];
            c2 = f.Dli.C2[row]; c3 = f.Dli.C3[row];
        }

        return new byte[]
        {
            back, c0, c1, (byte)(c0 | c1), 0, c2, c3, (byte)(c2 | c3)
        };
    }

    public bool IsPlayerActive(int p, bool layer01Visible, bool layer23Visible)
    {
        if (p < 2 && !layer01Visible) return false;
        if (p >= 2 && !layer23Visible) return false;
        return true;
    }

    /// <summary>Compute the displayed color index (0..7) at a cell.</summary>
    public int GetColorOn(int frameIdx, int col, int row, bool layer01Visible, bool layer23Visible)
    {
        if (frameIdx < 0 || frameIdx >= _ws.Frames.Count) return 0;
        if (col < 0 || col >= ColumnsActive) return 0;
        if (row < 0 || row >= _opt.SpriteHeight) return 0;

        var f = _ws.Frames[frameIdx];
        int b0 = f.Player[0][row], b1 = f.Player[1][row];
        int b2 = f.Player[2][row], b3 = f.Player[3][row];
        int bm0 = f.Missile[0][row], bm1 = f.Missile[1][row];
        int bm2 = f.Missile[2][row], bm3 = f.Missile[3][row];

        var m = GetMasks(col);
        int c0 = ((b0 & m[0]) != 0 || (bm0 & m[2]) != 0) ? 1 : 0;
        int c1 = ((b1 & m[1]) != 0 || (bm1 & m[3]) != 0) ? 2 : 0;
        int c01 = c0 | c1;
        int c2 = ((b2 & m[4]) != 0 || (bm2 & m[6]) != 0) ? 5 : 0;
        int c3 = ((b3 & m[5]) != 0 || (bm3 & m[7]) != 0) ? 6 : 0;
        int c23 = c2 | c3;

        int c = 0;
        if ((_opt.MergeMode.IsPlayer23Mode() || _opt.Freestyle) && layer23Visible)
        {
            if (c == 0) c = c23;
        }
        if (layer01Visible) c = c01 != 0 ? c01 : c;
        return c;
    }

    /// <summary>Paint a color index at (col,row) into the given frame, rewriting player/missile bytes.</summary>
    public void SetColorOn(int frameIdx, int col, int row, int color, bool layer01Visible, bool layer23Visible)
    {
        if (frameIdx < 0 || frameIdx >= _ws.Frames.Count) return;
        if (row < 0 || row >= _opt.SpriteHeight) return;
        var frame = _ws.Frames[frameIdx];
        var m = GetMasks(col);
        int mp0 = m[0], mp1 = m[1], mm0 = m[2], mm1 = m[3], mp2 = m[4], mp3 = m[5], mm2 = m[6], mm3 = m[7];

        void ClearPixel01()
        {
            frame.Player[0][row] &= (byte)(~mp0 & 0xFF);
            frame.Player[1][row] &= (byte)(~mp1 & 0xFF);
            frame.Missile[0][row] &= (byte)(~mm0 & 0xFF);
            frame.Missile[1][row] &= (byte)(~mm1 & 0xFF);
        }
        void ClearPixel23()
        {
            frame.Player[2][row] &= (byte)(~mp2 & 0xFF);
            frame.Player[3][row] &= (byte)(~mp3 & 0xFF);
            frame.Missile[2][row] &= (byte)(~mm2 & 0xFF);
            frame.Missile[3][row] &= (byte)(~mm3 & 0xFF);
        }
        void ClearAllExcept(int keep)
        {
            int[] pm = { mp0, mp1, mp2, mp3 };
            int[] mm = { mm0, mm1, mm2, mm3 };
            for (int i = 0; i < 4; i++)
            {
                if (i == keep) continue;
                frame.Player[i][row] &= (byte)(~pm[i] & 0xFF);
                frame.Missile[i][row] &= (byte)(~mm[i] & 0xFF);
            }
        }

        if (color == 0)
        {
            if (_opt.NoOverlap) { ClearPixel23(); ClearPixel01(); }
            else { if (layer23Visible) ClearPixel23(); if (layer01Visible) ClearPixel01(); }
        }
        if ((mp0 != 0 || mm0 != 0) && color == 1)
        {
            if (_opt.NoOverlap) ClearAllExcept(0);
            ClearPixel01();
            frame.Player[0][row] |= (byte)mp0;
            frame.Missile[0][row] |= (byte)mm0;
        }
        if ((mp1 != 0 || mm1 != 0) && color == 2)
        {
            if (_opt.NoOverlap) ClearAllExcept(1);
            ClearPixel01();
            frame.Player[1][row] |= (byte)mp1;
            frame.Missile[1][row] |= (byte)mm1;
        }
        if (color == 3)
        {
            if (_opt.OrDrawsOutside || ((mp0 != 0 || mm0 != 0) && (mp1 != 0 || mm1 != 0)))
            {
                ClearPixel01();
                frame.Player[0][row] |= (byte)mp0;
                frame.Player[1][row] |= (byte)mp1;
                frame.Missile[0][row] |= (byte)mm0;
                frame.Missile[1][row] |= (byte)mm1;
            }
        }
        if ((mp2 != 0 || mm2 != 0) && color == 5)
        {
            if (_opt.NoOverlap) ClearAllExcept(2);
            ClearPixel23();
            frame.Player[2][row] |= (byte)mp2;
            frame.Missile[2][row] |= (byte)mm2;
        }
        if ((mp3 != 0 || mm3 != 0) && color == 6)
        {
            if (_opt.NoOverlap) ClearAllExcept(3);
            ClearPixel23();
            frame.Player[3][row] |= (byte)mp3;
            frame.Missile[3][row] |= (byte)mm3;
        }
        if (color == 7)
        {
            if (_opt.OrDrawsOutside || ((mp2 != 0 || mm2 != 0) && (mp3 != 0 || mm3 != 0)))
            {
                ClearPixel23();
                frame.Player[2][row] |= (byte)mp2;
                frame.Player[3][row] |= (byte)mp3;
                frame.Missile[2][row] |= (byte)mm2;
                frame.Missile[3][row] |= (byte)mm3;
            }
        }
    }
}
