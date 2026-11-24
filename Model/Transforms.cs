using System;
using System.Collections.Generic;

namespace SpriteEditXL.Model;

/// <summary>
/// All frame-level transforms: flip H/V, swap pairs, move L/R/U/D, double/shrink height,
/// clear, copy/paste frame/colors/DLI, insert/delete row, clear overlaps.
/// Ported from transforms.js.
/// </summary>
public static class Transforms
{
    private static bool IsPlayerActive(int p, EditorSession s)
    {
        if (p < 2 && !s.Layer01Visible) return false;
        if (p >= 2 && !s.Layer23Visible) return false;
        return true;
    }

    private static (int first, int last) Range(Workspace w, Options o, EditorSession s) =>
        s.EffectiveRange(w.SelectedFrame, o.MultiFrameEdit);

    // -------- Color clipboard
    public static bool CopyColors(Workspace w, Options o, EditorSession s)
    {
        if (s.AnimationOn || o.CommonPalette) return false;
        var cb = new byte[8];
        if (o.DliOn)
        {
            var f = w.Frames[w.SelectedFrame];
            cb[5] = f.Dli.Back[w.SelectedDli];
            if (IsPlayerActive(0, s)) { cb[0] = f.Dli.C0[w.SelectedDli]; cb[1] = f.Dli.C1[w.SelectedDli]; }
            if (IsPlayerActive(2, s)) { cb[2] = f.Dli.C2[w.SelectedDli]; cb[3] = f.Dli.C3[w.SelectedDli]; }
        }
        else
        {
            for (int k = 0; k < 4; k++)
                if (IsPlayerActive(k, s)) cb[k] = w.Frames[w.SelectedFrame].Colors[k];
        }
        w.ClipBoard.Colors = cb;
        return true;
    }

    public static bool PasteColors(Workspace w, Options o, EditorSession s)
    {
        if (s.AnimationOn || o.CommonPalette || w.ClipBoard.Colors == null) return false;
        var cb = w.ClipBoard.Colors;
        if (o.DliOn)
        {
            var f = w.Frames[w.SelectedFrame];
            f.Dli.Back[w.SelectedDli] = cb[5];
            if (IsPlayerActive(0, s)) { f.Dli.C0[w.SelectedDli] = cb[0]; f.Dli.C1[w.SelectedDli] = cb[1]; }
            if (IsPlayerActive(2, s)) { f.Dli.C2[w.SelectedDli] = cb[2]; f.Dli.C3[w.SelectedDli] = cb[3]; }
        }
        else
        {
            for (int k = 0; k < 4; k++)
                if (IsPlayerActive(k, s)) w.Frames[w.SelectedFrame].Colors[k] = cb[k];
        }
        return true;
    }

    public static bool CopyDli(Workspace w, Options o, EditorSession s)
    {
        if (s.AnimationOn || o.CommonPalette || !o.DliOn) return false;
        w.ClipBoard.Dli = w.Frames[w.SelectedFrame].Dli.Clone();
        return true;
    }

    public static bool PasteDli(Workspace w, Options o, EditorSession s)
    {
        if (s.AnimationOn || o.CommonPalette || !o.DliOn || w.ClipBoard.Dli == null) return false;
        w.Frames[w.SelectedFrame].Dli = w.ClipBoard.Dli.Clone();
        return true;
    }

    // -------- Frame clipboard
    public static bool CopyFrame(Workspace w, EditorSession s)
    {
        if (s.AnimationOn) return false;
        w.ClipBoard.Frame = w.Frames[w.SelectedFrame].Clone();
        return true;
    }

    public static bool PasteFrame(Workspace w, Options o, EditorSession s)
    {
        if (s.AnimationOn || w.ClipBoard.Frame == null) return false;
        var (first, last) = Range(w, o, s);
        for (int f = first; f <= last; f++)
        {
            var src = w.ClipBoard.Frame;
            for (int k = 0; k < 4; k++)
            {
                if (!IsPlayerActive(k, s)) continue;
                w.Frames[f].Player[k] = (byte[])src.Player[k].Clone();
                w.Frames[f].Missile[k] = (byte[])src.Missile[k].Clone();
                w.Frames[f].Colors[k] = src.Colors[k];
            }
        }
        return true;
    }

    public static bool ClearFrame(Workspace w, Options o, EditorSession s)
    {
        if (s.AnimationOn) return false;
        var (first, last) = Range(w, o, s);
        for (int f = first; f <= last; f++)
        for (int r = 0; r < o.SpriteHeight; r++)
        for (int p = 0; p < 4; p++)
        {
            if (!IsPlayerActive(p, s)) continue;
            w.Frames[f].Player[p][r] = 0;
            w.Frames[f].Missile[p][r] = 0;
        }
        return true;
    }

    // -------- Swap pairs (P0/P1 <-> P2/P3)
    public static bool SwapPairs(Workspace w, Options o, EditorSession s)
    {
        if (s.AnimationOn || !o.MergeMode.IsPlayer23Mode()) return false;
        var (first, last) = Range(w, o, s);
        for (int f = first; f <= last; f++)
        {
            var cf = w.Frames[f];
            for (int p = 0; p < 2; p++)
            {
                (cf.Player[p], cf.Player[p + 2]) = (cf.Player[p + 2], cf.Player[p]);
                (cf.Missile[p], cf.Missile[p + 2]) = (cf.Missile[p + 2], cf.Missile[p]);
                if (!o.DliOn) (cf.Colors[p], cf.Colors[p + 2]) = (cf.Colors[p + 2], cf.Colors[p]);
            }
            if (o.DliOn)
            {
                (cf.Dli.C1, cf.Dli.C3) = (cf.Dli.C3, cf.Dli.C1);
                (cf.Dli.C2, cf.Dli.C0) = (cf.Dli.C0, cf.Dli.C2);
            }
        }
        return true;
    }

    // -------- Flip H (uses PixelEngine for freestyle; simple byte reverse otherwise)
    public static bool FlipH(Workspace w, Options o, EditorSession s)
    {
        if (s.AnimationOn || o.Freestyle) return false;
        var (first, last) = Range(w, o, s);
        bool missile = o.MergeMode.IsMissileMode();
        bool p23 = o.MergeMode.IsPlayer23Mode();

        for (int f = first; f <= last; f++)
        {
            var cf = w.Frames[f];
            for (int row = 0; row < o.SpriteHeight; row++)
            {
                if (!missile)
                {
                    byte[] b = {
                        Defines.ReversedBytes[cf.Player[0][row]],
                        Defines.ReversedBytes[cf.Player[1][row]],
                        Defines.ReversedBytes[cf.Player[2][row]],
                        Defines.ReversedBytes[cf.Player[3][row]]
                    };
                    (cf.Player[0][row], cf.Player[1][row]) = o.SpriteGap01 > 0 ? (b[1], b[0]) : (b[0], b[1]);
                    (cf.Player[2][row], cf.Player[3][row]) = o.SpriteGap23 > 0 ? (b[3], b[2]) : (b[2], b[3]);
                }
                else
                {
                    byte p0 = (byte)(Defines.ReversedBytes[(cf.Player[0][row] << 2) & 0xFF] | Defines.ReversedBytes[cf.Missile[0][row]]);
                    byte p1 = (byte)(Defines.ReversedBytes[(cf.Player[1][row] << 2) & 0xFF] | Defines.ReversedBytes[cf.Missile[1][row]]);
                    byte m0 = Defines.Reversed2Bits[cf.Player[0][row] >> 6];
                    byte m1 = Defines.Reversed2Bits[cf.Player[1][row] >> 6];
                    byte p2 = (byte)(Defines.ReversedBytes[(cf.Player[2][row] << 2) & 0xFF] | Defines.ReversedBytes[cf.Missile[2][row]]);
                    byte p3 = (byte)(Defines.ReversedBytes[(cf.Player[3][row] << 2) & 0xFF] | Defines.ReversedBytes[cf.Missile[3][row]]);
                    byte m2 = Defines.Reversed2Bits[cf.Player[2][row] >> 6];
                    byte m3 = Defines.Reversed2Bits[cf.Player[3][row] >> 6];

                    if (o.SpriteGap01 > 0) { cf.Player[0][row] = p1; cf.Player[1][row] = p0; cf.Missile[0][row] = m1; cf.Missile[1][row] = m0; }
                    else                   { cf.Player[0][row] = p0; cf.Player[1][row] = p1; cf.Missile[0][row] = m0; cf.Missile[1][row] = m1; }
                    if (o.SpriteGap23 > 0) { cf.Player[2][row] = p3; cf.Player[3][row] = p2; cf.Missile[2][row] = m3; cf.Missile[3][row] = m2; }
                    else                   { cf.Player[2][row] = p2; cf.Player[3][row] = p3; cf.Missile[2][row] = m2; cf.Missile[3][row] = m3; }
                }
            }

            if (o.SpriteGap01 > 0) (cf.Colors[0], cf.Colors[1]) = (cf.Colors[1], cf.Colors[0]);
            if (o.SpriteGap23 > 0) (cf.Colors[2], cf.Colors[3]) = (cf.Colors[3], cf.Colors[2]);

            if (p23 && o.PairGap > 0 && s.Layer01Visible && s.Layer23Visible)
            {
                (cf.Player[0], cf.Player[2]) = (cf.Player[2], cf.Player[0]);
                (cf.Player[1], cf.Player[3]) = (cf.Player[3], cf.Player[1]);
                if (missile)
                {
                    (cf.Missile[0], cf.Missile[2]) = (cf.Missile[2], cf.Missile[0]);
                    (cf.Missile[1], cf.Missile[3]) = (cf.Missile[3], cf.Missile[1]);
                }
                if (o.DliOn)
                {
                    (cf.Dli.C1, cf.Dli.C3) = (cf.Dli.C3, cf.Dli.C1);
                    (cf.Dli.C2, cf.Dli.C0) = (cf.Dli.C0, cf.Dli.C2);
                }
                else
                {
                    (cf.Colors[0], cf.Colors[2]) = (cf.Colors[2], cf.Colors[0]);
                    (cf.Colors[1], cf.Colors[3]) = (cf.Colors[3], cf.Colors[1]);
                }
            }
        }
        return true;
    }

    // -------- Flip V
    public static bool FlipV(Workspace w, Options o, EditorSession s)
    {
        if (s.AnimationOn || o.Freestyle) return false;
        var (first, last) = Range(w, o, s);
        int playerCount = o.MergeMode.PlayerCount();
        for (int f = first; f <= last; f++)
        {
            int lo = 0, hi = o.SpriteHeight - 1;
            while (lo < hi)
            {
                for (int p = 0; p < playerCount; p++)
                {
                    if (!IsPlayerActive(p, s)) continue;
                    (w.Frames[f].Player[p][lo], w.Frames[f].Player[p][hi]) = (w.Frames[f].Player[p][hi], w.Frames[f].Player[p][lo]);
                    (w.Frames[f].Missile[p][lo], w.Frames[f].Missile[p][hi]) = (w.Frames[f].Missile[p][hi], w.Frames[f].Missile[p][lo]);
                }
                if (o.DliOn)
                {
                    var d = w.Frames[f].Dli;
                    (d.C0[lo], d.C0[hi]) = (d.C0[hi], d.C0[lo]);
                    (d.C1[lo], d.C1[hi]) = (d.C1[hi], d.C1[lo]);
                    (d.C2[lo], d.C2[hi]) = (d.C2[hi], d.C2[lo]);
                    (d.C3[lo], d.C3[hi]) = (d.C3[hi], d.C3[lo]);
                }
                lo++; hi--;
            }
        }
        return true;
    }

    // -------- Vertical moves
    public static bool MoveUp(Workspace w, Options o, EditorSession s)
    {
        if (s.AnimationOn) return false;
        var (first, last) = Range(w, o, s);
        int maxP = o.Freestyle ? 4 : o.MergeMode.PlayerCount();
        for (int f = first; f <= last; f++)
        {
            for (int p = 0; p < maxP; p++)
            {
                if (!IsPlayerActive(p, s)) continue;
                var pf = w.Frames[f];
                byte top = pf.Player[p][0];
                Array.Copy(pf.Player[p], 1, pf.Player[p], 0, o.SpriteHeight - 1);
                pf.Player[p][o.SpriteHeight - 1] = o.WrapEditor ? top : (byte)0;
                top = pf.Missile[p][0];
                Array.Copy(pf.Missile[p], 1, pf.Missile[p], 0, o.SpriteHeight - 1);
                pf.Missile[p][o.SpriteHeight - 1] = o.WrapEditor ? top : (byte)0;
            }
            if (o.DliOn) RollDliUp(w.Frames[f].Dli, o);
        }
        return true;
    }

    public static bool MoveDown(Workspace w, Options o, EditorSession s)
    {
        if (s.AnimationOn) return false;
        var (first, last) = Range(w, o, s);
        int maxP = o.Freestyle ? 4 : o.MergeMode.PlayerCount();
        for (int f = first; f <= last; f++)
        {
            for (int p = 0; p < maxP; p++)
            {
                if (!IsPlayerActive(p, s)) continue;
                var pf = w.Frames[f];
                byte bottom = pf.Player[p][o.SpriteHeight - 1];
                Array.Copy(pf.Player[p], 0, pf.Player[p], 1, o.SpriteHeight - 1);
                pf.Player[p][0] = o.WrapEditor ? bottom : (byte)0;
                bottom = pf.Missile[p][o.SpriteHeight - 1];
                Array.Copy(pf.Missile[p], 0, pf.Missile[p], 1, o.SpriteHeight - 1);
                pf.Missile[p][0] = o.WrapEditor ? bottom : (byte)0;
            }
            if (o.DliOn) RollDliDown(w.Frames[f].Dli, o);
        }
        return true;
    }

    private static void RollDliUp(DliTable d, Options o)
    {
        foreach (var arr in new[] { d.Back, d.C0, d.C1, d.C2, d.C3 })
        {
            byte top = arr[0];
            Array.Copy(arr, 1, arr, 0, o.SpriteHeight - 1);
            arr[o.SpriteHeight - 1] = o.WrapEditor ? top : arr[0];
        }
    }

    private static void RollDliDown(DliTable d, Options o)
    {
        foreach (var arr in new[] { d.Back, d.C0, d.C1, d.C2, d.C3 })
        {
            byte bot = arr[o.SpriteHeight - 1];
            Array.Copy(arr, 0, arr, 1, o.SpriteHeight - 1);
            arr[0] = o.WrapEditor ? bot : arr[arr.Length - 1];
        }
    }

    // -------- Horizontal moves (uses PixelEngine for freestyle-aware masks)
    public static bool MoveLeft(Workspace w, Options o, EditorSession s)  => MoveFrame(w, o, s, -1);
    public static bool MoveRight(Workspace w, Options o, EditorSession s) => MoveFrame(w, o, s, +1);

    private static bool MoveFrame(Workspace w, Options o, EditorSession s, int dir)
    {
        if (s.AnimationOn || o.Freestyle) return false;
        var engine = new PixelEngine(w, o);
        int cols = engine.ColumnsActive;
        var (first, last) = Range(w, o, s);
        bool prev = o.OrDrawsOutside;
        o.OrDrawsOutside = true;
        try
        {
            for (int f = first; f <= last; f++)
            {
                var original = w.Frames[f].Clone();
                // clear this frame in place
                for (int r = 0; r < o.SpriteHeight; r++)
                for (int p = 0; p < 4; p++) { w.Frames[f].Player[p][r] = 0; w.Frames[f].Missile[p][r] = 0; }

                // Need an engine that reads from original — temporarily install
                int tmpIdx = w.Frames.Count;
                w.Frames.Add(original);
                var tempEngine = new PixelEngine(w, o);
                for (int row = 0; row < o.SpriteHeight; row++)
                {
                    for (int col = 0; col < cols; col++)
                    {
                        int target = col + dir;
                        bool clear = false;
                        if (target < 0) { if (!o.WrapEditor) clear = true; target = cols - 1; }
                        if (target == cols) { if (!o.WrapEditor) clear = true; target = 0; }
                        int c = tempEngine.GetColorOn(tmpIdx, col, row, s.Layer01Visible, s.Layer23Visible);
                        engine.SetColorOn(f, target, row, clear ? 0 : c, s.Layer01Visible, s.Layer23Visible);
                    }
                }
                w.Frames.RemoveAt(tmpIdx);
            }
        }
        finally { o.OrDrawsOutside = prev; }
        return true;
    }

    // -------- Height up/down (duplicate every row, or drop every second)
    public static bool HeightUp(Workspace w, Options o, EditorSession s)
    {
        if (s.AnimationOn) return false;
        var (first, last) = Range(w, o, s);
        int maxP = o.Freestyle ? 4 : o.MergeMode.PlayerCount();
        for (int f = first; f <= last; f++)
        {
            for (int p = 0; p < maxP; p++)
            {
                if (!IsPlayerActive(p, s)) continue;
                w.Frames[f].Player[p] = Duplicate(w.Frames[f].Player[p], o.SpriteHeight);
                w.Frames[f].Missile[p] = Duplicate(w.Frames[f].Missile[p], o.SpriteHeight);
            }
        }
        return true;
    }

    public static bool HeightDown(Workspace w, Options o, EditorSession s)
    {
        if (s.AnimationOn) return false;
        var (first, last) = Range(w, o, s);
        int maxP = o.Freestyle ? 4 : o.MergeMode.PlayerCount();
        for (int f = first; f <= last; f++)
        {
            for (int p = 0; p < maxP; p++)
            {
                if (!IsPlayerActive(p, s)) continue;
                w.Frames[f].Player[p] = Decimate(w.Frames[f].Player[p], o.SpriteHeight);
                w.Frames[f].Missile[p] = Decimate(w.Frames[f].Missile[p], o.SpriteHeight);
            }
        }
        return true;
    }

    private static byte[] Duplicate(byte[] src, int fixedLen)
    {
        var dst = new byte[fixedLen];
        for (int i = 0, j = 0; i < fixedLen && j < src.Length; j++)
        {
            if (i < fixedLen) dst[i++] = src[j];
            if (i < fixedLen) dst[i++] = src[j];
        }
        return dst;
    }

    private static byte[] Decimate(byte[] src, int fixedLen)
    {
        var dst = new byte[fixedLen];
        for (int i = 0, j = 0; j < src.Length && i < fixedLen; j += 2) dst[i++] = src[j];
        return dst;
    }

    // -------- Insert/delete row
    public static bool InsertLineAt(Workspace w, Options o, EditorSession s, int row)
    {
        if (s.AnimationOn) return false;
        row = Defines.Clamp(row, 0, o.SpriteHeight - 1);
        var (first, last) = Range(w, o, s);
        int maxP = o.Freestyle ? 4 : o.MergeMode.PlayerCount();
        for (int f = first; f <= last; f++)
        {
            for (int p = 0; p < maxP; p++)
            {
                if (!IsPlayerActive(p, s) || w.HiddenSprites[p]) continue;
                InsertByte(w.Frames[f].Player[p], row, 0);
                InsertByte(w.Frames[f].Missile[p], row, 0);
            }
            if (o.DliOn)
            {
                var d = w.Frames[f].Dli;
                byte Pick(byte[] a) => row > 0 ? a[row - 1] : a[row];
                InsertByte(d.Back, row, Pick(d.Back));
                InsertByte(d.C0, row, Pick(d.C0));
                InsertByte(d.C1, row, Pick(d.C1));
                InsertByte(d.C2, row, Pick(d.C2));
                InsertByte(d.C3, row, Pick(d.C3));
            }
        }
        return true;
    }

    public static bool DeleteLineAt(Workspace w, Options o, EditorSession s, int row)
    {
        if (s.AnimationOn) return false;
        row = Defines.Clamp(row, 0, o.SpriteHeight - 1);
        var (first, last) = Range(w, o, s);
        int maxP = o.Freestyle ? 4 : o.MergeMode.PlayerCount();
        for (int f = first; f <= last; f++)
        {
            for (int p = 0; p < maxP; p++)
            {
                if (!IsPlayerActive(p, s) || w.HiddenSprites[p]) continue;
                RemoveByte(w.Frames[f].Player[p], row);
                RemoveByte(w.Frames[f].Missile[p], row);
            }
            if (o.DliOn)
            {
                var d = w.Frames[f].Dli;
                RemoveByte(d.Back, row);
                RemoveByte(d.C0, row);
                RemoveByte(d.C1, row);
                RemoveByte(d.C2, row);
                RemoveByte(d.C3, row);
            }
        }
        return true;
    }

    private static void InsertByte(byte[] arr, int at, byte val)
    {
        Array.Copy(arr, at, arr, at + 1, arr.Length - at - 1);
        arr[at] = val;
    }

    private static void RemoveByte(byte[] arr, int at)
    {
        Array.Copy(arr, at + 1, arr, at, arr.Length - at - 1);
        arr[arr.Length - 1] = 0;
    }

    // -------- Clear overlaps
    public static bool ClearOverlaps(Workspace w, Options o, EditorSession s)
    {
        if (s.AnimationOn) return false;
        var (first, last) = Range(w, o, s);
        var engine = new PixelEngine(w, o);
        int cols = engine.ColumnsActive;
        for (int f = first; f <= last; f++)
        {
            var frame = w.Frames[f];
            for (int row = 0; row < o.SpriteHeight; row++)
            for (int col = 0; col < cols; col++)
            {
                var m = engine.GetMasks(col);
                int p0 = frame.Player[0][row] & m[0], mm0 = frame.Missile[0][row] & m[2];
                int p1 = frame.Player[1][row] & m[1], mm1 = frame.Missile[1][row] & m[3];
                int p2 = frame.Player[2][row] & m[4], mm2 = frame.Missile[2][row] & m[6];
                int p3 = frame.Player[3][row] & m[5], mm3 = frame.Missile[3][row] & m[7];
                if ((p1 | mm1) != 0)
                {
                    frame.Player[0][row] &= (byte)(~m[0] & 0xFF); frame.Missile[0][row] &= (byte)(~m[2] & 0xFF);
                    frame.Player[3][row] &= (byte)(~m[5] & 0xFF); frame.Missile[3][row] &= (byte)(~m[7] & 0xFF);
                    frame.Player[2][row] &= (byte)(~m[4] & 0xFF); frame.Missile[2][row] &= (byte)(~m[6] & 0xFF);
                    continue;
                }
                if ((p0 | mm0) != 0)
                {
                    frame.Player[3][row] &= (byte)(~m[5] & 0xFF); frame.Missile[3][row] &= (byte)(~m[7] & 0xFF);
                    frame.Player[2][row] &= (byte)(~m[4] & 0xFF); frame.Missile[2][row] &= (byte)(~m[6] & 0xFF);
                    continue;
                }
                if ((p3 | mm3) != 0)
                {
                    frame.Player[2][row] &= (byte)(~m[4] & 0xFF); frame.Missile[2][row] &= (byte)(~m[6] & 0xFF);
                }
            }
        }
        return true;
    }
}
