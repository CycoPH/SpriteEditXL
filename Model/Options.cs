namespace SpriteEditXL.Model;

public enum PaletteKind { PAL, NTSC }
public enum BytesExportFormat { HEX, DEC, BIN }

/// <summary>
/// Editor-wide options. Full parity with defaultOptions from defines.js.
/// </summary>
public sealed class Options
{
    public string Version { get; set; } = Defines.Version;
    public string StorageName { get; set; } = "SpriteEditXLStore";
    public int UndoLevels { get; set; } = 128;
    public int SpriteTemplate { get; set; } = -1;
    public int LineResolution { get; set; } = 1;
    public int SpriteHeight { get; set; } = 16;
    public int SpriteGap01 { get; set; }
    public int SpriteGap23 { get; set; }
    public int PairGap { get; set; } = 8;
    public bool MarkActiveRegion { get; set; } = true;
    public int ShowGrid { get; set; } = 1;
    public int CellSize { get; set; } = 5;
    public bool WrapEditor { get; set; } = true;
    public int AnimationSpeed { get; set; } = 5;
    public PaletteKind Palette { get; set; } = PaletteKind.PAL;
    public bool CommonPalette { get; set; }
    public BytesExportFormat BytesExport { get; set; } = BytesExportFormat.HEX;
    public int BytesPerLine { get; set; } = 16;
    public int LastTemplate { get; set; }
    public int StartingLine { get; set; } = 10000;
    public int LineStep { get; set; } = 10;
    public bool OrDrawsOutside { get; set; } = true;
    public bool NoOverlap { get; set; }
    public bool SquarePixel { get; set; } = true;
    public SpriteMode MergeMode { get; set; } = SpriteMode.P0P1;
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
    public bool MultiFrameEdit { get; set; } = true;
    public bool DrawOnPlay { get; set; }
    public bool LinkColors { get; set; }
    public bool FrameDelayMode { get; set; }
    public int GifExportScale { get; set; } = 4;
    public string LabelPrefix { get; set; } = "";
    public int BackOffsetH { get; set; }
    public int BackOffsetV { get; set; }
    public int BackOffset { get; set; }
    public int BackImageWidth { get; set; }
    public bool ShowBackground { get; set; }

    // Reference image (arbitrary PNG/GIF/JPG/BMP) pan + scale.
    /// <summary>Show the reference image behind transparent sprite cells.</summary>
    public bool ShowReference { get; set; }
    /// <summary>Source X offset (in reference pixels) that maps to the editor's top-left cell.</summary>
    public int ReferenceSourceX { get; set; }
    /// <summary>Source Y offset (in reference pixels) mapped to top-left cell.</summary>
    public int ReferenceSourceY { get; set; }
    /// <summary>How many reference pixels correspond to one sprite column (1 = pixel-perfect).</summary>
    public int ReferenceScaleX { get; set; } = 1;
    /// <summary>How many reference pixels correspond to one sprite row.</summary>
    public int ReferenceScaleY { get; set; } = 1;

    /// <summary>Anonymous per-install identity for library uploads. Minted once and persisted.</summary>
    public string UploaderId { get; set; } = "";
    /// <summary>Display name for library uploads. Optional; user fills it in on first upload.</summary>
    public string DefaultAuthorName { get; set; } = "";

    public Options Clone() => (Options)MemberwiseClone();
}
