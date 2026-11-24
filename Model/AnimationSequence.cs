using System.Collections.Generic;

namespace SpriteEditXL.Model;

/// <summary>
/// An ordered animation sequence over the primary frame pool.
/// Each entry is an index into <see cref="Workspace.Frames"/>.
/// A workspace always owns a "Primary" sequence whose indices are 0..N-1 of the pool.
/// </summary>
public sealed class AnimationSequence
{
    public const string PrimaryName = "Primary";

    public string Name { get; set; } = "";
    public List<int> FrameIndices { get; set; } = new();

    public bool IsPrimary => Name == PrimaryName;

    public AnimationSequence Clone() => new()
    {
        Name = Name,
        FrameIndices = new List<int>(FrameIndices)
    };
}
