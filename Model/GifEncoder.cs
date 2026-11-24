using System;
using System.Collections.Generic;
using System.IO;

namespace SpriteEditXL.Model;

/// <summary>
/// Minimal animated-GIF encoder (GIF89a) with a 256-entry global palette.
/// Writes directly to a stream; LZW compression is per the GIF spec.
/// </summary>
public sealed class GifEncoder : IDisposable
{
    private readonly Stream _s;
    private bool _wroteHeader;

    public GifEncoder(Stream s) { _s = s; }

    public void WriteGlobalPalette(IReadOnlyList<Rgb> palette256, int width, int height, int loopCount = 0)
    {
        if (palette256.Count != 256)
            throw new ArgumentException("palette must have 256 entries", nameof(palette256));
        _s.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }); // GIF89a
        WriteShort(width); WriteShort(height);
        _s.WriteByte(0xF7);     // GCT present, 256 entries, 8bpp
        _s.WriteByte(0);        // bg color index
        _s.WriteByte(0);        // aspect

        for (int i = 0; i < 256; i++)
        {
            _s.WriteByte(palette256[i].R);
            _s.WriteByte(palette256[i].G);
            _s.WriteByte(palette256[i].B);
        }

        // NETSCAPE2.0 looping extension
        _s.Write(new byte[] { 0x21, 0xFF, 0x0B });
        _s.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        _s.Write(new byte[] { 0x03, 0x01 });
        WriteShort(loopCount);
        _s.WriteByte(0);
        _wroteHeader = true;
    }

    /// <summary>Append a frame. delayHundredths is in 1/100 sec.</summary>
    public void WriteFrame(byte[] indexes, int width, int height, int delayHundredths)
    {
        if (!_wroteHeader) throw new InvalidOperationException("call WriteGlobalPalette first");
        // Graphic Control Extension
        _s.WriteByte(0x21); _s.WriteByte(0xF9); _s.WriteByte(0x04);
        _s.WriteByte(0x00); // no transparency, dispose = none
        WriteShort(delayHundredths);
        _s.WriteByte(0); _s.WriteByte(0);

        // Image Descriptor
        _s.WriteByte(0x2C);
        WriteShort(0); WriteShort(0);
        WriteShort(width); WriteShort(height);
        _s.WriteByte(0);

        // LZW
        _s.WriteByte(8);
        Lzw(indexes, 8, _s);
    }

    public void Finish() { _s.WriteByte(0x3B); }

    public void Dispose() { Finish(); _s.Flush(); }

    private void WriteShort(int v)
    {
        _s.WriteByte((byte)(v & 0xFF));
        _s.WriteByte((byte)((v >> 8) & 0xFF));
    }

    private static void Lzw(byte[] data, int codeSize, Stream dest)
    {
        int clearCode = 1 << codeSize;       // e.g. 256
        int endCode   = clearCode + 1;       // 257
        int nextCode  = endCode + 1;         // 258
        int currentCodeSize = codeSize + 1;  // 9

        var dict = new Dictionary<uint, int>();

        var outBits = new BitWriter();
        outBits.Write(clearCode, currentCodeSize);

        if (data.Length == 0)
        {
            outBits.Write(endCode, currentCodeSize);
            FlushBlocks(outBits.ToArray(), dest);
            return;
        }

        uint current = data[0];
        for (int i = 1; i < data.Length; i++)
        {
            uint next = (uint)(((long)current << 8) | data[i]);
            if (dict.TryGetValue(next, out int code))
            {
                current = (uint)code;
            }
            else
            {
                outBits.Write((int)current, currentCodeSize);
                dict[next] = nextCode;

                // Spec: increase code size BEFORE writing a code whose value would
                // overflow the current width. Trigger when assigning a code equal
                // to (1 << currentCodeSize).
                if (nextCode == (1 << currentCodeSize) && currentCodeSize < 12)
                {
                    currentCodeSize++;
                }

                nextCode++;

                // Full dictionary → emit clear and reset.
                if (nextCode == 4096)
                {
                    outBits.Write(clearCode, currentCodeSize);
                    dict.Clear();
                    nextCode = endCode + 1;
                    currentCodeSize = codeSize + 1;
                }
                current = data[i];
            }
        }
        outBits.Write((int)current, currentCodeSize);
        outBits.Write(endCode, currentCodeSize);
        FlushBlocks(outBits.ToArray(), dest);
    }

    private static void FlushBlocks(byte[] bytes, Stream dest)
    {
        int ofs = 0;
        while (ofs < bytes.Length)
        {
            int blockSize = Math.Min(255, bytes.Length - ofs);
            dest.WriteByte((byte)blockSize);
            dest.Write(bytes, ofs, blockSize);
            ofs += blockSize;
        }
        dest.WriteByte(0);
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = new();
        private int _buffer;
        private int _bufferBits;

        public void Write(int value, int bits)
        {
            _buffer |= (value & ((1 << bits) - 1)) << _bufferBits;
            _bufferBits += bits;
            while (_bufferBits >= 8)
            {
                _bytes.Add((byte)(_buffer & 0xFF));
                _buffer >>= 8;
                _bufferBits -= 8;
            }
        }

        public byte[] ToArray()
        {
            if (_bufferBits > 0) _bytes.Add((byte)(_buffer & 0xFF));
            return _bytes.ToArray();
        }
    }
}

public static class GifExport
{
    /// <summary>
    /// Export all frames as an animated GIF. Uses a global 256-entry Atari palette (indexed by cval),
    /// which means each sprite pixel becomes one byte — compact and loss-free.
    /// </summary>
    public static void WriteAnimation(string path, Workspace w, Options o, EditorSession s, int scale)
    {
        using var fs = File.Create(path);
        WriteAnimationToStream(fs, w, o, s, scale);
    }

    /// <summary>Same as <see cref="WriteAnimation"/> but writes to any stream (for in-memory use).</summary>
    public static void WriteAnimationToStream(Stream target, Workspace w, Options o, EditorSession s, int scale)
    {
        if (scale < 1) scale = 1;
        var size = FrameRenderer.GetGridSize(w, o);
        int gridW = size.Cols, gridH = size.Rows;

        int cellW = scale;
        int cellH = Math.Max(1, scale / o.LineResolution);
        int width = gridW * cellW;
        int height = gridH * cellH;

        var palette = new Rgb[256];
        for (int i = 0; i < 256; i++) palette[i] = AtariPalette.ToRgb((byte)i, o.Palette);

        var engine = new PixelEngine(w, o);
        using var gif = new GifEncoder(target);
        gif.WriteGlobalPalette(palette, width, height);

        var indices = new byte[width * height];
        for (int fi = 0; fi < w.Frames.Count; fi++)
        {
            RenderFrameIndices(w, o, s, engine, fi, gridW, gridH, cellW, cellH, indices);
            int delay = o.FrameDelayMode ? w.Frames[fi].DelayTime : o.AnimationSpeed;
            gif.WriteFrame(indices, width, height, delay * 2); // frames * 20ms -> hundredths
        }
    }

    private static void RenderFrameIndices(Workspace w, Options o, EditorSession s, PixelEngine engine, int fi, int gridW, int gridH, int cellW, int cellH, byte[] dst)
    {
        for (int r = 0; r < gridH; r++)
        {
            int dataRow = r - o.BackOffsetV;
            var colors = engine.GetColors(fi, dataRow);
            for (int c = 0; c < gridW; c++)
            {
                int dataCol = c - o.BackOffsetH;
                byte cval;
                if (dataRow >= 0 && dataRow < o.SpriteHeight && dataCol >= 0 && dataCol < engine.ColumnsActive)
                {
                    int ci = engine.GetColorOn(fi, dataCol, dataRow, s.Layer01Visible, s.Layer23Visible);
                    cval = colors[ci];
                }
                else cval = w.BackgroundColor;

                for (int yy = 0; yy < cellH; yy++)
                {
                    int row = (r * cellH + yy) * gridW * cellW + c * cellW;
                    for (int xx = 0; xx < cellW; xx++) dst[row + xx] = cval;
                }
            }
        }
    }
}
