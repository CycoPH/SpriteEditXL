using System;
using System.Collections.Generic;
using System.Text;

namespace SpriteEditXL.Model;

// Template placeholders match the JS version's #tokens#:
// #frames# #height# #gap01# #gap23# #pairgap# #maxheight# #maxframes# #-1# #-2# #isntsc#
// #f# (frame no), #s# (sprite no), #d# (dli name), #lp# (label prefix)

public sealed class ExportTemplate
{
    public string Name { get; init; } = "";
    public int Mode { get; init; }               // 0=by sprite, 1=by frame
    public string BlockPrefix { get; init; } = "";
    public string? BlockPrefix23 { get; init; }
    public string BlockPostfix { get; init; } = "";
    public string ColorsPrefix { get; init; } = "";
    public string ColorsPostfix { get; init; } = "";
    public string SpritePrefix { get; init; } = "";
    public string SpritePostfix { get; init; } = "";
    public string MissilesPrefix { get; init; } = "";
    public string MissilesPostfix { get; init; } = "";
    public string OffsetsPrefix { get; init; } = "";
    public string OffsetsPostfix { get; init; } = "";
    public string DliPrefix { get; init; } = "";
    public string DliPostfix { get; init; } = "";
    public string FramePrefix { get; init; } = "";
    public string FramePostfix { get; init; } = "";
    public string? FrameNoLabelPrefix { get; init; }
    public string FrameDelaysPrefix { get; init; } = "";
    public string FrameDelaysPostfix { get; init; } = "";
    public bool   LineNumbers { get; init; }
    public string LinePrefix { get; init; } = "";
    public string LinePostfix { get; init; } = "\n";
    public string? LineLastPostfix { get; init; }
    public bool   PreserveLastSeparator { get; init; }
    public string ByteSeparator { get; init; } = ", ";
    public string ByteHexPrefix { get; init; } = "$";
    public string ByteBinPrefix { get; init; } = "%";
    public string ByteAddrPrefix { get; init; } = "a(";
    public string ByteAddrPostfix { get; init; } = ")";
    public BytesExportFormat? ByteForceNumeric { get; init; }
    public bool ByteForceUpper { get; init; }
}

public static class ExportTemplates
{
    public static readonly IReadOnlyList<ExportTemplate> All = new ExportTemplate[]
    {
        new()
        {
            Name = "MADS Assembler", Mode = 0,
            BlockPrefix   = "; SPRITE DATA\n; frames, height, gap\n  dta #frames#,#height#,#gap01#\n\n",
            BlockPrefix23 = "; SPRITE DATA\n; frames, height, gap0_1, gap2_3, pair_gap\n  dta #frames#,#height#,#gap01#,#gap23#,#pairgap#\n\n",
            ColorsPrefix = "; SPRITE COLORS #s#\n#lp#COL_#s#\n",
            SpritePrefix = "\n; SPRITE #s#\n",
            MissilesPrefix = "\n; MISSILES \n",
            OffsetsPrefix = "\n; OFFSETS (p0,p1,p2,p3,m0,m1,m2,m3)\n",
            DliPrefix = "\n; DLI #d#\n",
            FramePrefix = "; FRAME #f#\n#lp#SPR_#s#_FRM_#f#\n",
            FrameNoLabelPrefix = "; FRAME #f#\n",
            FrameDelaysPrefix = "\n; DELAY TIMES \n",
            LineNumbers = false, LinePrefix = "  dta ", LinePostfix = "\n",
            ByteSeparator = ", ", ByteBinPrefix = "%", ByteHexPrefix = "$"
        },
        new()
        {
            Name = "MADS Assembler (.he notation)", Mode = 0,
            BlockPrefix   = "; SPRITE DATA\n; frames, height, gap\n  .he #frames# #height# #gap01#\n\n",
            BlockPrefix23 = "; SPRITE DATA\n; frames, height, gap0_1, gap2_3, pair_gap\n  .he #frames# #height# #gap01# #gap23# #pairgap#\n\n",
            ColorsPrefix = "; SPRITE COLORS #s#\n#lp#COL_#s#\n",
            SpritePrefix = "\n; SPRITE #s#\n",
            MissilesPrefix = "\n; MISSILES \n",
            OffsetsPrefix = "\n; OFFSETS (p0,p1,p2,p3,m0,m1,m2,m3)\n",
            DliPrefix = "\n; DLI #d#\n",
            FramePrefix = "; FRAME #f#\n#lp#SPR_#s#_FRM_#f#\n",
            FrameNoLabelPrefix = "; FRAME #f#\n",
            FrameDelaysPrefix = "\n; DELAY TIMES \n",
            LineNumbers = false, LinePrefix = "  .he ", LinePostfix = "\n",
            ByteSeparator = " ", ByteBinPrefix = "", ByteHexPrefix = "",
            ByteForceNumeric = BytesExportFormat.HEX
        },
        new()
        {
            Name = "ATASM", Mode = 0,
            BlockPrefix   = "; SPRITE DATA\n; frames, height, gap\n  .byte #frames#,#height#,#gap01#\n\n",
            BlockPrefix23 = "; SPRITE DATA\n; frames, height, gap0_1, gap2_3, pair_gap\n  .byte #frames#,#height#,#gap01#,#gap23#,#pairgap#\n\n",
            ColorsPrefix = "; SPRITE COLORS #s#\n#lp#COL_#s#\n",
            SpritePrefix = "\n; SPRITE #s#\n",
            MissilesPrefix = "\n; MISSILES \n",
            OffsetsPrefix = "\n; OFFSETS (p0,p1,p2,p3,m0,m1,m2,m3)\n",
            DliPrefix = "\n; DLI #d#\n",
            FramePrefix = "; FRAME #f#\n#lp#SPR_#s#_FRM_#f#\n",
            FrameNoLabelPrefix = "; FRAME #f#\n",
            FrameDelaysPrefix = "\n; DELAY TIMES \n",
            LineNumbers = false, LinePrefix = "  .byte ", LinePostfix = "\n",
            ByteSeparator = ",", ByteBinPrefix = "%", ByteHexPrefix = "$"
        },
        new()
        {
            Name = "Other Assemblers", Mode = 0,
            BlockPrefix   = "; SPRITE DATA\n; frames, height, gap\n  .BYTE #frames#,#height#,#gap01#\n\n",
            BlockPrefix23 = "; SPRITE DATA\n; frames, height, gap0_1, gap2_3, pair_gap\n  .BYTE #frames#,#height#,#gap01#,#gap23#,#pairgap#\n\n",
            ColorsPrefix = "; SPRITE COLORS #s#\n#lp#COL_#s#\n",
            SpritePrefix = "\n; SPRITE #s#\n",
            MissilesPrefix = "\n; MISSILES \n",
            OffsetsPrefix = "\n; OFFSETS (p0,p1,p2,p3,m0,m1,m2,m3)\n",
            DliPrefix = "\n; DLI #d#\n",
            FramePrefix = "; FRAME #f#\n#lp#SPR_#s#_FRM_#f#\n",
            FrameNoLabelPrefix = "; FRAME #f#\n",
            FrameDelaysPrefix = "\n; DELAY TIMES \n",
            LineNumbers = false, LinePrefix = "  .BYTE ", LinePostfix = "\n",
            ByteSeparator = ", ", ByteBinPrefix = "%", ByteHexPrefix = "$"
        },
        new()
        {
            Name = "MAC/65", Mode = 0,
            BlockPrefix   = "#-1# .BYTE #frames#,#height#,#gap01# ;frames, height, gap\n",
            BlockPrefix23 = "#-1# .BYTE #frames#,#height#,#gap01#,#gap23#,#pairgap# ;frames, height, gap0_1, gap2_3, pair_gap\n",
            OffsetsPrefix = "\n; OFFSETS (p0,p1,p2,p3,m0,m1,m2,m3)\n",
            LineNumbers = true, LinePrefix = ".BYTE ", LinePostfix = "\n",
            ByteSeparator = ",", ByteHexPrefix = "$", ByteBinPrefix = "%"
        },
        new()
        {
            Name = "Mad-Pascal", Mode = 0,
            BlockPrefix   = "var\n  spriteFrames: byte = #frames#;\n  spriteHeight: byte = #height#;\n  spriteGap: byte = #gap01#;\n\n",
            BlockPrefix23 = "var\n  spriteFrames: byte = #frames#;\n  spriteHeight: byte = #height#;\n  spriteGap01: byte = #gap01#;\n  spriteGap23: byte = #gap23#;\n  pairGap: byte = #pairgap#;\n\n",
            ColorsPrefix = "  #lp#colors#s#: array [0..#maxframes#] of byte = (\n", ColorsPostfix = "  );\n",
            SpritePrefix = "\n// sprite #s# data\n",
            MissilesPrefix = "\n// missiles\n",
            OffsetsPrefix = "\n// offsets\n  #lp#OFFSETS: array [0..7] of byte = (\n", OffsetsPostfix = "  );\n",
            DliPrefix = "\n// dli #d#\n",
            FramePrefix = "  #lp#frames#d##s#_#f#: array [0..#maxheight#] of byte = (\n", FramePostfix = "  );\n",
            FrameDelaysPrefix = "\n// delay Times\n",
            LineNumbers = false, LinePrefix = "    ", LinePostfix = ",\n", LineLastPostfix = "\n",
            ByteSeparator = ", ", ByteHexPrefix = "$", ByteBinPrefix = "%"
        },
        new()
        {
            Name = "Action!", Mode = 0,
            BlockPrefix   = "BYTE FRAMES=#frames#,\n     HEIGHT=#height#,\n     GAP=#gap01#;\n\n",
            BlockPrefix23 = "BYTE FRAMES=#frames#,\n     HEIGHT=#height#,\n     GAP01=#gap01#,\n     GAP23=#gap23#,\n     PAIRGAP=#pairgap#,\n\n",
            ColorsPrefix = "BYTE ARRAY #lp#COLORS#s# =[\n", ColorsPostfix = "]\n",
            SpritePrefix = "\n; sprite #s#\n",
            MissilesPrefix = "\n; missiles\n",
            OffsetsPrefix = "\nBYTE ARRAY #lp#OFFSETS =[\n", OffsetsPostfix = "]\n",
            DliPrefix = "\n; dli #d#\n",
            FramePrefix = "BYTE ARRAY #lp#FRAMES#d##s#_#f# =[\n", FramePostfix = "]\n",
            FrameDelaysPrefix = "\n; delay times\n",
            LineNumbers = false, LinePrefix = "  ", LinePostfix = "\n",
            ByteSeparator = " ", ByteHexPrefix = "$", ByteForceUpper = true
        },
        new()
        {
            Name = "CC65", Mode = 0,
            BlockPrefix   = "unsigned char frames = #frames#;\nunsigned char height = #height#;\nunsigned char gap = #gap01#,\n\n",
            BlockPrefix23 = "unsigned char frames = #frames#;\nunsigned char height = #height#;\nunsigned char gap01 = #gap01#,\nunsigned char gap23 = #gap23#,\nunsigned char pairgap = #pairgap#,\n\n",
            ColorsPrefix = "unsigned char #lp#colors#s#_#f#[#height#] = {\n", ColorsPostfix = "};",
            SpritePrefix = "\n\n//sprite #s#\n",
            MissilesPrefix = "\n\n//missiles\n",
            OffsetsPrefix = "\n\n//offsets\nunsigned char #lp#offsets[8] = {\n", OffsetsPostfix = "};\n",
            DliPrefix = "\n\n//dli #d#\n",
            FramePrefix = "unsigned char #lp#frames#d##s#_#f#[#height#] = {\n", FramePostfix = "};\n",
            FrameDelaysPrefix = "\n\n//delay times\n",
            LineNumbers = false, LinePrefix = "    ", LinePostfix = ",\n", LineLastPostfix = "\n",
            ByteSeparator = ", ", ByteHexPrefix = "0x", ByteBinPrefix = "0b"
        },
        new()
        {
            Name = "FastBasic", Mode = 0,
            BlockPrefix   = "frames = #frames#\nheight = #height#\ngap = #gap01#\n\n",
            BlockPrefix23 = "frames = #frames#\nheight = #height#\ngap01 = #gap01#\ngap23 = #gap23#\npairgap = #pairgap#\n\n",
            ColorsPrefix = "data #lp#colors#s#()",
            SpritePrefix = "\n'sprite #s#\n",
            MissilesPrefix = "\n'missiles\n",
            OffsetsPrefix = "\n'offsets\n#lp#offsets()",
            DliPrefix = "\n'dli #d#\n",
            FramePrefix = "data #lp#frame#d##s#_#f#()",
            FrameDelaysPrefix = "\n'delay times\n",
            LineNumbers = false, LinePrefix = " byte = ", LinePostfix = ",\ndata", LineLastPostfix = "\n",
            ByteSeparator = ", ", ByteHexPrefix = "$"
        },
        new()
        {
            Name = "BASIC", Mode = 0,
            BlockPrefix   = "#-2# FRAMES=#frames#:HEIGHT=#height#:GAP=#gap01#\n#-1# REM *** SPRITE DATA ***\n",
            BlockPrefix23 = "#-2# FRAMES=#frames#:HEIGHT=#height#:GAP01=#gap01#:GAP23=#gap23#:PAIRGAP=#pairgap#\n#-1# REM *** SPRITE DATA ***\n",
            OffsetsPrefix = "#-1# REM OFFSETS\n",
            LineNumbers = true, LinePrefix = "DATA ", LinePostfix = "\n",
            ByteSeparator = ",", ByteHexPrefix = "",
            ByteForceNumeric = BytesExportFormat.DEC
        },
        new()
        {
            Name = "Raw CSV sheet", Mode = 0,
            BlockPrefix   = "#frames#,#height#,#gap01#\n",
            BlockPrefix23 = "#frames#,#height#,#gap01#,#gap23#,#pairgap#\n",
            LineNumbers = false, LinePrefix = "", LinePostfix = "\n",
            ByteSeparator = ",", ByteHexPrefix = "$"
        },
        new()
        {
            Name = "Raw CSV one liner", Mode = 0,
            BlockPrefix   = "#frames#,#height#,#gap01#,",
            BlockPrefix23 = "#frames#,#height#,#gap01#,#gap23#,#pairgap#,",
            LineNumbers = false, LinePrefix = "", LinePostfix = "", PreserveLastSeparator = true,
            ByteSeparator = ",", ByteHexPrefix = "$"
        }
    };
}

/// <summary>Renders a workspace through an export template. Ports js/export.js parseTemplate.</summary>
public sealed class Exporter
{
    private readonly Workspace _w;
    private readonly Options _o;

    private readonly ExportTemplate _t;
    private int _byteInRow;
    private int _lineCount;
    private readonly StringBuilder _lineBody = new();
    private readonly StringBuilder _lines = new();
    private readonly StringBuilder _templateLines = new();
    private int _tframe;
    private string _tsprite = "";
    private string _tdli = "";

    public Exporter(ExportTemplate t, Workspace w, Options o)
    {
        _t = t; _w = w; _o = o;
    }

    public static string Render(ExportTemplate t, Workspace w, Options o) =>
        new Exporter(t, w, o).Build();

    private string Build()
    {
        int playerCount = _o.MergeMode.PlayerCount();

        if (_t.Mode == 0)
        {
            var pList = new List<int>();
            if (_o.Freestyle)
            {
                int[] poff = { _o.P0Offset, _o.P1Offset, _o.P2Offset, _o.P3Offset };
                for (int i = 0; i < 4; i++) if (poff[i] >= 0) pList.Add(i);
            }
            else for (int i = 0; i < playerCount; i++) pList.Add(i);

            foreach (var p in pList) PushSpriteColors(p);
            foreach (var p in pList) PushSpriteData(p);
            if (_o.MergeMode.IsMissileMode()) PushMissileData();
            if (_o.DliOn) PushDliData();
            if (_o.FrameDelayMode) PushFrameDelays();
            PushOffsets();

            var block = _o.MergeMode.IsPlayer23Mode() && _t.BlockPrefix23 != null
                ? _t.BlockPrefix23 : _t.BlockPrefix;
            return ParseVars(block + _templateLines + _t.BlockPostfix);
        }
        else // Mode == 1: by frame
        {
            for (int f = 0; f < _w.Frames.Count; f++)
            {
                _tframe = f;
                _lines.Clear();
                var frame = _w.Frames[f];
                var data = new List<byte>(_o.SpriteHeight * 3 + 2);
                for (int r = 0; r < _o.SpriteHeight; r++) data.Add(frame.Player[0][r]);
                for (int r = 0; r < _o.SpriteHeight; r++) data.Add(frame.Player[1][r]);
                for (int r = 0; r < _o.SpriteHeight; r++)
                    data.Add((byte)(frame.Missile[0][r] | (frame.Missile[1][r] << 2)));
                data.Add(frame.Colors[0]);
                data.Add(frame.Colors[1]);
                PushArray(data);
                var sprite = GetBlock(_lines.ToString(), _t.SpritePrefix, _t.SpritePostfix);
                PushBlock(sprite, _t.FramePrefix, _t.FramePostfix);
            }
            PushOffsets();
            var block = _o.MergeMode.IsPlayer23Mode() && _t.BlockPrefix23 != null
                ? _t.BlockPrefix23 : _t.BlockPrefix;
            return ParseVars(block + _templateLines + _t.BlockPostfix);
        }
    }

    private void PushSpriteColors(int s)
    {
        _lines.Clear();
        _tsprite = s.ToString();
        var arr = new List<byte>(_w.Frames.Count);
        foreach (var f in _w.Frames) arr.Add(f.Colors[s]);
        PushArray(arr);
        PushBlock(_lines.ToString(), _t.ColorsPrefix, _t.ColorsPostfix);
    }

    private void PushSpriteData(int s)
    {
        var sprite = new StringBuilder();
        _tsprite = s.ToString();
        for (int f = 0; f < _w.Frames.Count; f++)
        {
            _tframe = f;
            _lines.Clear();
            var merged = new byte[_o.SpriteHeight];
            var frame = _w.Frames[f];
            bool missileLeft = _o.MergeMode.IsMissileOnLeft();
            for (int r = 0; r < _o.SpriteHeight; r++)
                merged[r] = missileLeft ? (byte)((frame.Player[s][r] << 2) | frame.Missile[s][r]) : frame.Player[s][r];
            PushArray(merged);
            sprite.Append(GetBlock(_lines.ToString(), _t.FramePrefix, _t.FramePostfix));
        }
        PushBlock(sprite.ToString(), _t.SpritePrefix, _t.SpritePostfix);
    }

    private void PushMissileData()
    {
        var missiles = new StringBuilder();
        _tsprite = "MIS";
        int maxP = _o.Freestyle ? 4 : _o.MergeMode.PlayerCount();
        int[] offs = { _o.M0Offset, _o.M1Offset, _o.M2Offset, _o.M3Offset };
        for (int f = 0; f < _w.Frames.Count; f++)
        {
            _tframe = f;
            _lines.Clear();
            var merged = new byte[_o.SpriteHeight];
            var frame = _w.Frames[f];
            bool missileLeft = _o.MergeMode.IsMissileOnLeft();
            for (int p = 0; p < maxP; p++)
            {
                if (_o.Freestyle && offs[p] < 0) continue;
                for (int r = 0; r < _o.SpriteHeight; r++)
                {
                    byte bit = missileLeft
                        ? (byte)((frame.Player[p][r] >> 6) & 3)
                        : frame.Missile[p][r];
                    merged[r] |= (byte)((bit & 3) << (p * 2));
                }
            }
            PushArray(merged);
            missiles.Append(GetBlock(_lines.ToString(), _t.FramePrefix, _t.FramePostfix));
        }
        PushBlock(missiles.ToString(), _t.MissilesPrefix, _t.MissilesPostfix);
    }

    private void PushDliData()
    {
        _tsprite = "";
        bool p23 = _o.MergeMode.IsPlayer23Mode() || _o.Freestyle;
        string[] names = p23 ? Defines.DliMap : new[] { "back", "c0", "c1" };
        foreach (var name in names)
        {
            _tdli = name.ToUpperInvariant();
            var dli = new StringBuilder();
            for (int f = 0; f < _w.Frames.Count; f++)
            {
                _tframe = f;
                _lines.Clear();
                byte[] arr = name switch
                {
                    "back" => _w.Frames[f].Dli.Back,
                    "c0"   => _w.Frames[f].Dli.C0,
                    "c1"   => _w.Frames[f].Dli.C1,
                    "c2"   => _w.Frames[f].Dli.C2,
                    _      => _w.Frames[f].Dli.C3,
                };
                PushArray(arr);
                var prefix = _t.FrameNoLabelPrefix ?? _t.FramePrefix;
                dli.Append(GetBlock(_lines.ToString(), prefix, _t.FramePostfix));
            }
            PushBlock(dli.ToString(), _t.DliPrefix, _t.DliPostfix);
        }
    }

    private void PushFrameDelays()
    {
        _lines.Clear();
        var arr = new byte[_w.Frames.Count];
        for (int i = 0; i < arr.Length; i++) arr[i] = (byte)_w.Frames[i].DelayTime;
        PushArray(arr);
        PushBlock(_lines.ToString(), _t.FrameDelaysPrefix, _t.FrameDelaysPostfix);
    }

    private void PushOffsets()
    {
        if (!_o.Freestyle || string.IsNullOrEmpty(_t.OffsetsPrefix)) return;
        int[] raw = { _o.P0Offset, _o.P1Offset, _o.P2Offset, _o.P3Offset,
                      _o.M0Offset, _o.M1Offset, _o.M2Offset, _o.M3Offset };
        var arr = new byte[8];
        for (int i = 0; i < 8; i++) arr[i] = (byte)(raw[i] < 0 ? 255 : (raw[i] & 0xFF));
        _lines.Clear();
        PushArray(arr);
        PushBlock(_lines.ToString(), _t.OffsetsPrefix, _t.OffsetsPostfix);
    }

    private void PushArray(IReadOnlyList<byte> arr)
    {
        for (int i = 0; i < arr.Count; i++) PushByte(arr[i], i == arr.Count - 1);
        if (_byteInRow > 0) PushLine(_lineBody.ToString(), true);
    }

    private void PushByte(byte b, bool last)
    {
        _lineBody.Append(FormatByte(b));
        StepByte(last);
    }

    private void StepByte(bool last)
    {
        _byteInRow++;
        if (_byteInRow == _o.BytesPerLine || last)
        {
            if (_t.PreserveLastSeparator) _lineBody.Append(_t.ByteSeparator);
            _byteInRow = 0;
            PushLine(_lineBody.ToString(), last);
            _lineBody.Clear();
        }
        else _lineBody.Append(_t.ByteSeparator);
    }

    private void PushLine(string body, bool last)
    {
        string num = _t.LineNumbers ? $"{_o.StartingLine + _o.LineStep * _lineCount} " : "";
        _lineCount++;
        _lines.Append(num).Append(_t.LinePrefix).Append(body)
              .Append(last ? (_t.LineLastPostfix ?? _t.LinePostfix) : _t.LinePostfix);
        _byteInRow = 0;
        _lineBody.Clear();
    }

    private string FormatByte(byte b)
    {
        var fmt = _t.ByteForceNumeric ?? _o.BytesExport;
        if (fmt == BytesExportFormat.HEX)
        {
            var o = $"{_t.ByteHexPrefix}{b:x2}";
            return _t.ByteForceUpper ? o.ToUpperInvariant() : o;
        }
        if (fmt == BytesExportFormat.BIN) return $"{_t.ByteBinPrefix}{Convert.ToString(b, 2).PadLeft(8, '0')}";
        return b.ToString();
    }

    private string GetBlock(string body, string prefix, string postfix)
        => (prefix + body + postfix)
            .Replace("#f#", _tframe.ToString())
            .Replace("#s#", _tsprite)
            .Replace("#d#", _tdli)
            .Replace("#lp#", _o.LabelPrefix);

    private void PushBlock(string body, string prefix, string postfix)
        => _templateLines.Append(GetBlock(body, prefix, postfix));

    private string ParseVars(string s)
    {
        return s
            .Replace("#height#",    FormatByte((byte)_o.SpriteHeight))
            .Replace("#gap01#",     FormatByte((byte)_o.SpriteGap01))
            .Replace("#gap23#",     FormatByte((byte)_o.SpriteGap23))
            .Replace("#pairgap#",   FormatByte((byte)_o.PairGap))
            .Replace("#frames#",    FormatByte((byte)_w.Frames.Count))
            .Replace("#maxheight#", FormatByte((byte)(_o.SpriteHeight - 1)))
            .Replace("#maxframes#", FormatByte((byte)(_w.Frames.Count - 1)))
            .Replace("#-1#",        (_o.StartingLine - 1).ToString())
            .Replace("#-2#",        (_o.StartingLine - 2).ToString())
            .Replace("#isntsc#",    FormatByte((byte)(_o.Palette == PaletteKind.NTSC ? 1 : 0)));
    }
}
