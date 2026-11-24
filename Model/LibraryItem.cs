using System;

namespace SpriteEditXL.Model;

/// <summary>Where a <see cref="LibraryItem"/> came from.</summary>
public enum LibrarySourceKind { Local, SprEdRemote }

/// <summary>
/// A sprite entry listed in the Library dialog. Lightweight — the actual bitmap data
/// isn't loaded until the user picks this item.
/// </summary>
public sealed class LibraryItem
{
    public LibrarySourceKind Source { get; init; }
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public int FrameCount { get; init; }
    public DateTime UploadDate { get; init; }

    /// <summary>Local library path (if <see cref="Source"/> == Local).</summary>
    public string? LocalPath { get; init; }

    /// <summary>restdb.io record id (if <see cref="Source"/> == SprEdRemote).</summary>
    public string? RemoteId { get; init; }

    /// <summary>Optional pre-rendered preview thumbnail as a BGRA buffer.</summary>
    public byte[]? PreviewBgra { get; init; }
    public int PreviewWidth { get; init; }
    public int PreviewHeight { get; init; }

    /// <summary>
    /// A "data:image/gif;base64,..." URL carrying the uploaded animated preview.
    /// Used by the SprEd community and SpriteEditXL remote sources.
    /// </summary>
    public string? PreviewDataUrl { get; init; }
}
