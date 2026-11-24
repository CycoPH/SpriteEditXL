using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpriteEditXL.Model;

namespace SpriteEditXL.IO;

/// <summary>
/// Primary JSON file format for SpriteEditXL. Contains the whole Workspace plus the
/// Options that are meaningful for reproducing the sprite (height, mode, flags, gaps…).
/// Pure UI preferences (zoom, grid, reference image state) are NOT stored here —
/// they live in options.json next to the app.
/// </summary>
public static class SprJsonFile
{
    public const int CurrentVersion = 1;
    public const string DefaultExtension = ".sprj";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public sealed class Bundle
    {
        public int Version { get; set; } = CurrentVersion;
        public Workspace Workspace { get; set; } = new();
        public SpriteOptions SpriteOptions { get; set; } = new();
    }

    /// <summary>
    /// The subset of <see cref="Options"/> that defines the sprite itself.
    /// UI preferences (zoom, grid, palettes-vs-NTSC view, reference pan) live elsewhere.
    /// </summary>
    public sealed class SpriteOptions
    {
        public int SpriteHeight { get; set; } = 16;
        public int LineResolution { get; set; } = 1;
        public SpriteMode MergeMode { get; set; } = SpriteMode.P0P1;
        public int SpriteGap01 { get; set; }
        public int SpriteGap23 { get; set; }
        public int PairGap { get; set; } = 8;
        public bool Freestyle { get; set; }
        public int P0Offset { get; set; }
        public int P1Offset { get; set; } = 8;
        public int P2Offset { get; set; } = 16;
        public int P3Offset { get; set; } = 24;
        public int M0Offset { get; set; } = -1;
        public int M1Offset { get; set; } = -1;
        public int M2Offset { get; set; } = -1;
        public int M3Offset { get; set; } = -1;
        public bool DliOn { get; set; }
        public bool CommonPalette { get; set; }
        public bool FrameDelayMode { get; set; }
        public int AnimationSpeed { get; set; } = 5;
        public PaletteKind Palette { get; set; } = PaletteKind.PAL;

        public static SpriteOptions From(Options o) => new()
        {
            SpriteHeight = o.SpriteHeight,
            LineResolution = o.LineResolution,
            MergeMode = o.MergeMode,
            SpriteGap01 = o.SpriteGap01,
            SpriteGap23 = o.SpriteGap23,
            PairGap = o.PairGap,
            Freestyle = o.Freestyle,
            P0Offset = o.P0Offset,
            P1Offset = o.P1Offset,
            P2Offset = o.P2Offset,
            P3Offset = o.P3Offset,
            M0Offset = o.M0Offset,
            M1Offset = o.M1Offset,
            M2Offset = o.M2Offset,
            M3Offset = o.M3Offset,
            DliOn = o.DliOn,
            CommonPalette = o.CommonPalette,
            FrameDelayMode = o.FrameDelayMode,
            AnimationSpeed = o.AnimationSpeed,
            Palette = o.Palette
        };

        public void ApplyTo(Options o)
        {
            o.SpriteHeight = SpriteHeight;
            o.LineResolution = LineResolution;
            o.MergeMode = MergeMode;
            o.SpriteGap01 = SpriteGap01;
            o.SpriteGap23 = SpriteGap23;
            o.PairGap = PairGap;
            o.Freestyle = Freestyle;
            o.P0Offset = P0Offset;
            o.P1Offset = P1Offset;
            o.P2Offset = P2Offset;
            o.P3Offset = P3Offset;
            o.M0Offset = M0Offset;
            o.M1Offset = M1Offset;
            o.M2Offset = M2Offset;
            o.M3Offset = M3Offset;
            o.DliOn = DliOn;
            o.CommonPalette = CommonPalette;
            o.FrameDelayMode = FrameDelayMode;
            o.AnimationSpeed = AnimationSpeed;
            o.Palette = Palette;
        }
    }

    public static void Save(string path, Workspace ws, Options opt)
    {
        ws.SyncPrimary();
        var bundle = new Bundle
        {
            Version = CurrentVersion,
            Workspace = ws,
            SpriteOptions = SpriteOptions.From(opt)
        };
        File.WriteAllText(path, JsonSerializer.Serialize(bundle, JsonOpts));
    }

    public static (Workspace ws, Options opt) Load(string path)
    {
        var text = File.ReadAllText(path);
        var bundle = JsonSerializer.Deserialize<Bundle>(text, JsonOpts)
                     ?? throw new InvalidDataException("empty or malformed .sprj file");
        var ws = bundle.Workspace ?? new Workspace();
        var opt = new Options();
        bundle.SpriteOptions?.ApplyTo(opt);
        ws.SyncPrimary();
        return (ws, opt);
    }
}
