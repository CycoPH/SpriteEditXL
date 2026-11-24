using System;
using System.Collections.Generic;
using System.Linq;

namespace SpriteEditXL.Model;

public sealed class ClipBoard
{
    public byte[]? Colors { get; set; }
    public DliTable? Dli { get; set; }
    public Frame? Frame { get; set; }

    public ClipBoard Clone() => new()
    {
        Colors = Colors == null ? null : (byte[])Colors.Clone(),
        Dli = Dli?.Clone(),
        Frame = Frame?.Clone()
    };
}

public sealed class Workspace
{
    public int SelectedColor { get; set; } = 1;
    public int SelectedFrame { get; set; }
    public byte BackgroundColor { get; set; }
    public int SelectedDli { get; set; }
    public List<Frame> Frames { get; set; } = new();
    public bool[] HiddenSprites { get; set; } = new bool[4];
    public ClipBoard ClipBoard { get; set; } = new();

    /// <summary>Atari 2bpp palette-indexed background (SprEd-compatible format).</summary>
    public byte[]? BackgroundImage { get; set; }

    /// <summary>
    /// Raw reference image loaded from any common format (PNG, GIF, JPEG, BMP).
    /// Stored as BGRA8888 bytes so we can blit it directly onto a WriteableBitmap.
    /// NOT persisted to workspace.json (would bloat it) — see <see cref="ReferenceImagePath"/>
    /// for the file reload path on next startup.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[]? ReferenceImage { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int ReferenceImageWidth { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int ReferenceImageHeight { get; set; }

    /// <summary>File path from which the reference image was loaded; reloaded on next startup.</summary>
    public string? ReferenceImagePath { get; set; }

    /// <summary>
    /// Named animation sequences. Always contains a "Primary" sequence whose
    /// FrameIndices is kept in sync with 0..Frames.Count-1 by <see cref="SyncPrimary"/>.
    /// Custom sequences hold references (indices) into the primary frame pool.
    /// </summary>
    public List<AnimationSequence> Sequences { get; set; } = new();

    /// <summary>Index into <see cref="Sequences"/>. 0 = Primary.</summary>
    public int SelectedSequence { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Frame CurrentFrame => Frames[Math.Clamp(SelectedFrame, 0, Math.Max(0, Frames.Count - 1))];

    /// <summary>Ensure Primary exists and mirrors the frame pool. Clamp indices in custom sequences.</summary>
    public void SyncPrimary()
    {
        if (Sequences.Count == 0 || Sequences[0].Name != AnimationSequence.PrimaryName)
            Sequences.Insert(0, new AnimationSequence { Name = AnimationSequence.PrimaryName });
        var primary = Sequences[0];
        primary.FrameIndices = Enumerable.Range(0, Frames.Count).ToList();

        // Clean out dangling references in custom sequences.
        for (int s = 1; s < Sequences.Count; s++)
            Sequences[s].FrameIndices.RemoveAll(i => i < 0 || i >= Frames.Count);

        if (SelectedSequence < 0 || SelectedSequence >= Sequences.Count) SelectedSequence = 0;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public AnimationSequence ActiveSequence
    {
        get
        {
            SyncPrimary();
            return Sequences[Math.Clamp(SelectedSequence, 0, Sequences.Count - 1)];
        }
    }

    public Workspace Clone()
    {
        var w = new Workspace
        {
            SelectedColor = SelectedColor,
            SelectedFrame = SelectedFrame,
            BackgroundColor = BackgroundColor,
            SelectedDli = SelectedDli,
            HiddenSprites = (bool[])HiddenSprites.Clone(),
            ClipBoard = ClipBoard.Clone(),
            BackgroundImage = BackgroundImage == null ? null : (byte[])BackgroundImage.Clone(),
            ReferenceImage = ReferenceImage == null ? null : (byte[])ReferenceImage.Clone(),
            ReferenceImageWidth = ReferenceImageWidth,
            ReferenceImageHeight = ReferenceImageHeight,
            ReferenceImagePath = ReferenceImagePath,
            SelectedSequence = SelectedSequence
        };
        foreach (var f in Frames) w.Frames.Add(f.Clone());
        foreach (var s in Sequences) w.Sequences.Add(s.Clone());
        return w;
    }
}
