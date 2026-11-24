using System;
using System.Collections.Generic;
using System.IO;
using SpriteEditXL.Model;

namespace SpriteEditXL.IO;

/// <summary>
/// Reads/writes the SprEd SPR binary format plus legacy APL/APL+ imports.
/// Ported from js/io.js parseBinary() and saveFile().
/// </summary>
public static class SprFile
{
    public static void Save(string path, Workspace ws, Options opt)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);

        w.Write(Defines.SprHeader);
        w.Write((byte)ws.SelectedFrame);
        w.Write((byte)ws.SelectedColor);
        w.Write(ws.BackgroundColor);
        w.Write((byte)opt.AnimationSpeed);
        w.Write((byte)(opt.Palette == PaletteKind.PAL ? 0 : 1));
        w.Write((byte)opt.LineResolution);
        w.Write((byte)(int)opt.MergeMode);
        w.Write((byte)opt.SpriteGap01);
        w.Write((byte)opt.SpriteGap23);
        w.Write((byte)opt.PairGap);

        int flags = (opt.DliOn ? 1 : 0) | (opt.CommonPalette ? 2 : 0) | (opt.Freestyle ? 4 : 0);
        w.Write((byte)flags);
        w.Write((byte)(opt.FrameDelayMode ? 1 : 0));
        w.Write((byte)0);                    // unused
        w.Write((byte)0);                    // forces the "v2" layout — real frame count follows
        w.Write((byte)ws.Frames.Count);
        w.Write((byte)opt.SpriteHeight);

        int playerCount = opt.MergeMode.PlayerCount();

        // colors: for each player, one byte per frame
        for (int p = 0; p < playerCount; p++)
            foreach (var f in ws.Frames) w.Write(f.Colors[p]);

        // player bitmaps: for each player, spriteHeight bytes per frame
        for (int p = 0; p < playerCount; p++)
            foreach (var f in ws.Frames) WriteHeight(w, f.Player[p], opt.SpriteHeight);

        if (opt.MergeMode.IsMissileMode())
        {
            for (int p = 0; p < playerCount; p++)
                foreach (var f in ws.Frames) WriteHeight(w, f.Missile[p], opt.SpriteHeight);
        }

        if (opt.DliOn)
        {
            foreach (var f in ws.Frames) WriteHeight(w, f.Dli.Back, opt.SpriteHeight);
            foreach (var f in ws.Frames) WriteHeight(w, f.Dli.C0, opt.SpriteHeight);
            foreach (var f in ws.Frames) WriteHeight(w, f.Dli.C1, opt.SpriteHeight);
            foreach (var f in ws.Frames) WriteHeight(w, f.Dli.C2, opt.SpriteHeight);
            foreach (var f in ws.Frames) WriteHeight(w, f.Dli.C3, opt.SpriteHeight);
        }

        if (opt.FrameDelayMode)
            foreach (var f in ws.Frames) w.Write((byte)f.DelayTime);

        if (opt.Freestyle)
        {
            w.Write((byte)opt.P0Offset);
            w.Write((byte)opt.P1Offset);
            w.Write((byte)opt.P2Offset);
            w.Write((byte)opt.P3Offset);
            w.Write((byte)opt.M0Offset);
            w.Write((byte)opt.M1Offset);
            w.Write((byte)opt.M2Offset);
            w.Write((byte)opt.M3Offset);
        }

        File.WriteAllBytes(path, ms.ToArray());
    }

    public static (Workspace ws, Options opt) Load(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 4) throw new InvalidDataException("file too small");

        if (HeaderEquals(data, Defines.AplHeader))  return LoadApl(data, 48);
        if (HeaderEquals(data, Defines.Apl2Header)) return LoadApl(data, 52);
        if (HeaderEquals(data, Defines.SprHeader))  return LoadSpr(data);
        throw new InvalidDataException("unknown format");
    }

    private static (Workspace, Options) LoadSpr(byte[] data)
    {
        var opt = new Options();
        var ws = new Workspace();
        int ptr = 4;

        byte ReadByte()
        {
            if (ptr >= data.Length)
                throw new InvalidDataException(
                    $"SPR file ended earlier than expected (read past byte {data.Length}). " +
                    "The file may be truncated or the mode/flags lead the loader to expect more data than is present.");
            return data[ptr++];
        }

        ws.SelectedFrame = ReadByte();
        ws.SelectedColor = ReadByte();
        ws.BackgroundColor = ReadByte();
        opt.AnimationSpeed = ReadByte();
        opt.Palette = ReadByte() == 1 ? PaletteKind.NTSC : PaletteKind.PAL;
        opt.LineResolution = ReadByte();
        byte rawMode = ReadByte();
        opt.MergeMode = (SpriteMode)rawMode;
        opt.SpriteGap01 = ReadByte();
        opt.SpriteGap23 = ReadByte();
        opt.PairGap = ReadByte();
        int flags = ReadByte();
        opt.DliOn = (flags & 1) != 0;
        opt.CommonPalette = (flags & 2) != 0;
        bool hasFreestyle = (flags & 4) != 0;
        opt.FrameDelayMode = ReadByte() != 0;
        int frameCount = ReadByte();
        int formatVariant = 0;    // 0 = JS (1 unused byte); 1 = legacy P0P1; 2 = old-C# (2 unused bytes)

        if (frameCount != 0)
        {
            // Legacy P0+P1 layout: spriteHeight and spriteGap01 follow.
            formatVariant = 1;
            opt.SpriteHeight = ReadByte();
            opt.SpriteGap01 = ReadByte();
        }
        else
        {
            // frameCount byte was 0. In JS format the next byte IS the real frame count.
            // In the older C# format a SECOND zero byte comes first, then frame count.
            // Discriminate by checking if the next two bytes could both be the layout markers
            // of the older format (second zero + plausible frameCount).
            int probe1 = ReadByte();
            int probe2 = ReadByte();
            if (probe1 == 0 && probe2 > 0 && ptr < data.Length)
            {
                // Looks like old-C# layout: probe1 was the 2nd signal byte, probe2 is real frameCount,
                // and spriteHeight follows.
                formatVariant = 2;
                frameCount = probe2;
                opt.SpriteHeight = ReadByte();
            }
            else
            {
                // JS layout: probe1 IS the real frameCount, probe2 is spriteHeight.
                formatVariant = 0;
                frameCount = probe1;
                opt.SpriteHeight = probe2;
            }
        }

        int playerCount = opt.MergeMode.PlayerCount();

        for (int f = 0; f < frameCount; f++)
        {
            var frame = Frame.CreateEmpty(opt.SpriteHeight, ws.BackgroundColor);
            frame.Colors[0] = ReadByte();
            ws.Frames.Add(frame);
        }
        for (int p = 1; p < playerCount; p++)
            for (int f = 0; f < frameCount; f++)
                ws.Frames[f].Colors[p] = ReadByte();

        for (int p = 0; p < playerCount; p++)
            for (int f = 0; f < frameCount; f++)
                ptr = ReadHeight(data, ptr, ws.Frames[f].Player[p], opt.SpriteHeight);

        if (opt.MergeMode.IsMissileMode())
        {
            for (int p = 0; p < playerCount; p++)
                for (int f = 0; f < frameCount; f++)
                    ptr = ReadHeight(data, ptr, ws.Frames[f].Missile[p], opt.SpriteHeight);
        }

        if (opt.DliOn)
        {
            for (int f = 0; f < frameCount; f++) ptr = ReadHeight(data, ptr, ws.Frames[f].Dli.Back, opt.SpriteHeight);
            for (int f = 0; f < frameCount; f++) ptr = ReadHeight(data, ptr, ws.Frames[f].Dli.C0,   opt.SpriteHeight);
            for (int f = 0; f < frameCount; f++) ptr = ReadHeight(data, ptr, ws.Frames[f].Dli.C1,   opt.SpriteHeight);
            for (int f = 0; f < frameCount; f++) ptr = ReadHeight(data, ptr, ws.Frames[f].Dli.C2,   opt.SpriteHeight);
            for (int f = 0; f < frameCount; f++) ptr = ReadHeight(data, ptr, ws.Frames[f].Dli.C3,   opt.SpriteHeight);
        }

        if (opt.FrameDelayMode)
            for (int f = 0; f < frameCount; f++) ws.Frames[f].DelayTime = ReadByte();

        if (hasFreestyle)
        {
            opt.Freestyle = true;
            opt.P0Offset = (sbyte)ReadByte();
            opt.P1Offset = (sbyte)ReadByte();
            opt.P2Offset = (sbyte)ReadByte();
            opt.P3Offset = (sbyte)ReadByte();
            opt.M0Offset = (sbyte)ReadByte();
            opt.M1Offset = (sbyte)ReadByte();
            opt.M2Offset = (sbyte)ReadByte();
            opt.M3Offset = (sbyte)ReadByte();
        }

        return (ws, opt);
    }

    private static (Workspace, Options) LoadApl(byte[] data, int frameSize)
    {
        var opt = new Options { MergeMode = SpriteMode.P0P1, LineResolution = 2 };
        var ws = new Workspace();
        int ptr = 4;
        int aplFrames = data[ptr++];
        opt.SpriteHeight = data[ptr++];
        opt.SpriteGap01 = data[ptr++];

        for (int f = 0; f < 17; f++)
        {
            var frame = Frame.CreateEmpty(opt.SpriteHeight, 0);
            frame.Colors[0] = data[ptr++];
            ws.Frames.Add(frame);
        }
        for (int f = 0; f < 17; f++) ws.Frames[f].Colors[1] = data[ptr++];
        ws.BackgroundColor = data[ptr++];
        for (int f = 0; f < 17; f++) ptr = ReadBlock(data, ptr, ws.Frames[f].Player[0], frameSize, opt.SpriteHeight);
        for (int f = 0; f < 17; f++) ptr = ReadBlock(data, ptr, ws.Frames[f].Player[1], frameSize, opt.SpriteHeight);
        ws.SelectedFrame = data[ptr++];
        ws.SelectedColor = data[ptr++];
        opt.AnimationSpeed = data[ptr++];
        opt.Palette = data[ptr++] == 1 ? PaletteKind.NTSC : PaletteKind.PAL;
        if (aplFrames < ws.Frames.Count) ws.Frames.RemoveRange(aplFrames, ws.Frames.Count - aplFrames);
        return (ws, opt);
    }

    private static bool HeaderEquals(byte[] data, byte[] header)
    {
        if (data.Length < header.Length) return false;
        for (int i = 0; i < header.Length; i++) if (data[i] != header[i]) return false;
        return true;
    }

    private static void WriteHeight(BinaryWriter w, byte[] src, int height)
    {
        for (int i = 0; i < height; i++) w.Write(i < src.Length ? src[i] : (byte)0);
    }

    private static int ReadHeight(byte[] data, int ptr, byte[] dst, int height)
    {
        for (int i = 0; i < height; i++) dst[i] = ptr + i < data.Length ? data[ptr + i] : (byte)0;
        return ptr + height;
    }

    private static int ReadBlock(byte[] data, int ptr, byte[] dst, int blockSize, int useHeight)
    {
        int copy = Math.Min(useHeight, blockSize);
        for (int i = 0; i < copy; i++) dst[i] = data[ptr + i];
        return ptr + blockSize;
    }
}
