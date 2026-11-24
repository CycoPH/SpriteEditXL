namespace SpriteEditXL.Model;

/// <summary>
/// Transient per-session UI state (not serialized). Mirrors module-scope variables
/// from the JS source: layer01visible/layer23visible, movieLoop, dliRange, altPressed.
/// </summary>
public sealed class EditorSession
{
    public bool Layer01Visible { get; set; } = true;
    public bool Layer23Visible { get; set; } = true;

    /// <summary>[first, last] inclusive, or null.</summary>
    public (int First, int Last)? MovieLoop { get; set; }

    /// <summary>[first, last] inclusive DLI range for group color editing, or null.</summary>
    public (int First, int Last)? DliRange { get; set; }

    public bool AltPressed { get; set; }
    public bool AnimationOn { get; set; }

    public bool IsInMovieLoop(int f)
        => MovieLoop is { } m && f >= m.First && f <= m.Last;

    /// <summary>Range that effects should iterate over (multi-frame edit, or just selected frame).</summary>
    public (int First, int Last) EffectiveRange(int selectedFrame, bool multiFrameEdit)
        => (multiFrameEdit && MovieLoop is { } m) ? (m.First, m.Last) : (selectedFrame, selectedFrame);
}
