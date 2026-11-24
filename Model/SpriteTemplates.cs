using System.Collections.Generic;

namespace SpriteEditXL.Model;

/// <summary>Sprite layout presets ported from spriteTemplates.js.</summary>
public sealed class SpriteTemplate
{
    public string Name { get; init; } = "";
    public bool Freestyle { get; init; }
    public int LineResolution { get; init; } = 1;
    public SpriteMode MergeMode { get; init; } = SpriteMode.P0P1;
    public int SpriteHeight { get; init; } = 16;
    public int SpriteGap01 { get; init; }
    public int SpriteGap23 { get; init; }
    public int PairGap { get; init; }
    public int? P0Offset { get; init; }
    public int? P1Offset { get; init; }
    public int? P2Offset { get; init; }
    public int? P3Offset { get; init; }
    public int? M0Offset { get; init; }
    public int? M1Offset { get; init; }
    public int? M2Offset { get; init; }
    public int? M3Offset { get; init; }

    public void ApplyTo(Options opt)
    {
        opt.LineResolution = LineResolution;
        opt.SpriteHeight = SpriteHeight;
        opt.SpriteGap01 = SpriteGap01;
        opt.SpriteGap23 = SpriteGap23;
        opt.PairGap = PairGap;
        if (!Freestyle)
        {
            opt.MergeMode = MergeMode;
            opt.Freestyle = false;
        }
        else
        {
            opt.Freestyle = true;
            if (P0Offset.HasValue) opt.P0Offset = P0Offset.Value;
            if (P1Offset.HasValue) opt.P1Offset = P1Offset.Value;
            if (P2Offset.HasValue) opt.P2Offset = P2Offset.Value;
            if (P3Offset.HasValue) opt.P3Offset = P3Offset.Value;
            if (M0Offset.HasValue) opt.M0Offset = M0Offset.Value;
            if (M1Offset.HasValue) opt.M1Offset = M1Offset.Value;
            if (M2Offset.HasValue) opt.M2Offset = M2Offset.Value;
            if (M3Offset.HasValue) opt.M3Offset = M3Offset.Value;
        }
    }
}

public static class SpriteTemplates
{
    public static readonly IReadOnlyList<SpriteTemplate> All = new SpriteTemplate[]
    {
        new() { Name = "Simple 8x8 (square)",   LineResolution = 1, MergeMode = SpriteMode.P0P1,     SpriteHeight = 8  },
        new() { Name = "Simple 8x16 (brick)",   LineResolution = 2, MergeMode = SpriteMode.P0P1,     SpriteHeight = 16 },
        new() { Name = "aNode icon 10x20",      LineResolution = 2, MergeMode = SpriteMode.PM0PM1,   SpriteHeight = 20 },
        new() { Name = "Big 16x16 (square)",    LineResolution = 1, MergeMode = SpriteMode.P0P1,     SpriteHeight = 16, SpriteGap01 = 8 },
        new() { Name = "Wide 32x16 (square)",   LineResolution = 1, MergeMode = SpriteMode.P0P1P2P3, SpriteHeight = 16, SpriteGap01 = 8, SpriteGap23 = 8, PairGap = 16 },
        new() { Name = "SuperWide 40x20",       LineResolution = 1, MergeMode = SpriteMode.PM0PM1PM2PM3, SpriteHeight = 20, SpriteGap01 = 10, SpriteGap23 = 10, PairGap = 20 },
        new() { Name = "Freestyle Mode",        LineResolution = 1, Freestyle = true, SpriteHeight = 16,
                P0Offset = 0, P1Offset = 8, P2Offset = 16, P3Offset = 24,
                M0Offset = -1, M1Offset = -1, M2Offset = -1, M3Offset = -1 }
    };
}
