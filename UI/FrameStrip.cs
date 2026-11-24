using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SpriteEditXL.Model;

namespace SpriteEditXL.UI;

/// <summary>
/// Horizontal strip of frame thumbnails for ONE sequence.
/// Click a thumb selects the referenced pool frame.
/// Press+hold then move more than the drag-threshold to begin a drag — at that
/// point the host (MainWindow) takes over, drawing a ghost thumbnail that
/// follows the cursor and routing the drop to whichever strip receives it.
/// </summary>
public sealed class FrameStrip : Control
{
    private Workspace _ws = new();
    private Options _opt = new();
    private EditorSession _session = new();
    /// <summary>Index into <see cref="Workspace.Sequences"/> that this strip renders.</summary>
    public int SequenceIndex { get; set; }

    public const int ThumbWidth = 60;
    public const int ThumbHeight = 48;
    public const int Pad = 6;
    private WriteableBitmap? _stripBitmap;

    // Drag detection (threshold only; actual drag is handled by the host)
    private int _pressSlot = -1;
    private Point _pressPoint;
    private bool _dragReported;

    /// <summary>Slot currently being dragged (set by host while a cross-strip drag is in flight). -1 = none.</summary>
    public int ActiveDragSlot { get; set; } = -1;

    /// <summary>Insertion-gap index to highlight (host sets this during drag). -1 = none.</summary>
    public int ActiveDragInsertion { get; set; } = -1;

    /// <summary>
    /// Current slot position within this sequence's FrameIndices for wheel-scroll stepping.
    /// Tracked separately from Workspace.SelectedFrame so that repeated indices in a sequence
    /// (e.g. ping-pong animations like 17,18,19,20,21,20,19,18,17) are honored.
    /// Reset to the first occurrence of the selected frame whenever a click lands.
    /// </summary>
    private int _scrollCursor = -1;

    public event EventHandler? SelectionChanged;

    /// <summary>Raised when pointer movement exceeds the drag threshold and the host should begin global drag.</summary>
    public event EventHandler<FrameDragBeginEventArgs>? DragStarted;

    /// <summary>Raised when the user clicks the orange delay number below a frame thumbnail.
    /// The host should pop an input prompt and update <see cref="Frame.DelayTime"/>.</summary>
    public event EventHandler<DelayEditEventArgs>? DelayEditRequested;

    public FrameStrip()
    {
        Height = ThumbHeight + 24;
    }

    public void SetState(Workspace ws, Options opt, EditorSession session, int sequenceIndex)
    {
        _ws = ws; _opt = opt; _session = session; SequenceIndex = sequenceIndex;
        Rebuild();
    }

    /// <summary>Convert a point in this strip to the slot index (0..count-1), or -1 if outside.</summary>
    public int SlotAt(Point p)
    {
        int slot = (int)((p.X - Pad) / (ThumbWidth + Pad));
        var seq = CurrentSequence();
        if (seq == null) return -1;
        if (slot < 0 || slot >= seq.FrameIndices.Count) return -1;
        return slot;
    }

    /// <summary>Slot index clamped to insertion gap [0..count].</summary>
    public int InsertionSlotAt(Point p)
    {
        var seq = CurrentSequence();
        if (seq == null) return 0;
        // Gap k lives at x = k*(ThumbWidth+Pad). Round the pointer X to the nearest gap center.
        int raw = (int)Math.Round((p.X - Pad) / (ThumbWidth + Pad));
        return Math.Clamp(raw, 0, seq.FrameIndices.Count);
    }

    /// <summary>Return the on-screen rectangle for slot i, in this strip's local coordinates.</summary>
    public Rect SlotRect(int slot) =>
        new Rect(slot * (ThumbWidth + Pad) + Pad, 4, ThumbWidth, ThumbHeight);

    private AnimationSequence? CurrentSequence() =>
        (SequenceIndex >= 0 && SequenceIndex < _ws.Sequences.Count) ? _ws.Sequences[SequenceIndex] : null;

    public void Rebuild()
    {
        var seq = CurrentSequence();
        if (seq == null || _ws.Frames.Count == 0) { Width = Pad * 4; InvalidateVisual(); return; }

        int count = Math.Max(1, seq.FrameIndices.Count);
        int w = count * (ThumbWidth + Pad) + Pad;
        int h = ThumbHeight + 4;
        Width = w;

        _stripBitmap?.Dispose();
        _stripBitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Opaque);
        var size = FrameRenderer.GetGridSize(_ws, _opt);

        int cw = Math.Max(1, ThumbWidth / size.Cols);
        int ch = Math.Max(1, ThumbHeight / size.Rows);

        var buf = new byte[w * h * 4];
        for (int slot = 0; slot < seq.FrameIndices.Count; slot++)
        {
            int poolIdx = seq.FrameIndices[slot];
            if (poolIdx < 0 || poolIdx >= _ws.Frames.Count) continue;
            int ox = slot * (ThumbWidth + Pad) + Pad;
            var bytes = FrameRenderer.RenderBgra(_ws, _opt, _session, poolIdx, cw, ch, out int tw, out int th);
            for (int y = 0; y < th && y < h; y++)
            {
                int srcRow = y * tw * 4;
                int dstRow = (y * w + ox) * 4;
                int cols = Math.Min(tw, w - ox);
                Buffer.BlockCopy(bytes, srcRow, buf, dstRow, cols * 4);
            }
        }

        using var fb = _stripBitmap.Lock();
        unsafe
        {
            fixed (byte* src = buf)
                Buffer.MemoryCopy(src, (void*)fb.Address, buf.Length, buf.Length);
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        var seq = CurrentSequence();
        if (seq == null) return;
        if (_stripBitmap != null)
            ctx.DrawImage(_stripBitmap, new Rect(0, 4, _stripBitmap.PixelSize.Width, _stripBitmap.PixelSize.Height));

        var font = new Typeface("Inter");
        for (int slot = 0; slot < seq.FrameIndices.Count; slot++)
        {
            int poolIdx = seq.FrameIndices[slot];
            var rect = SlotRect(slot);

            bool isDragging = (slot == ActiveDragSlot);
            bool isSelected = _ws.SelectedSequence == SequenceIndex && poolIdx == _ws.SelectedFrame;
            Pen pen;
            if (isDragging)
            {
                // Make the dragged source thumb obviously highlighted (thick cyan outline, faded).
                pen = new Pen(Brushes.Cyan, 3);
                ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), rect);
            }
            else if (isSelected) pen = new Pen(Brushes.Yellow, 2);
            else if (_session.IsInMovieLoop(poolIdx)) pen = new Pen(Brushes.OrangeRed, 1.5);
            else pen = new Pen(Brushes.DimGray, 1);
            ctx.DrawRectangle(null, pen, rect);

            // Always show the pool frame index (the original frame number).
            var num = new FormattedText(poolIdx.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, font, 11, Brushes.White);
            ctx.DrawText(num, new Point(rect.X + 2, rect.Bottom + 2));
            if (_opt.FrameDelayMode)
            {
                var d = new FormattedText(_ws.Frames[poolIdx].DelayTime.ToString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, font, 10, Brushes.Orange);
                ctx.DrawText(d, new Point(rect.Right - 16, rect.Bottom + 3));
            }
        }

        // Insertion indicator (cyan vertical line between slots).
        if (ActiveDragInsertion >= 0)
        {
            double x = ActiveDragInsertion * (ThumbWidth + Pad) + Pad / 2.0;
            var indicator = new Pen(Brushes.Cyan, 3);
            ctx.DrawLine(indicator, new Point(x, 2), new Point(x, ThumbHeight + 6));
        }
    }

    /// <summary>Hit-test the orange delay number region below a slot. Returns -1 if outside.</summary>
    private int DelayHitAt(Point p)
    {
        if (!_opt.FrameDelayMode) return -1;
        var seq = CurrentSequence();
        if (seq == null) return -1;
        // The delay number is drawn at (rect.Right - 16, rect.Bottom + 3) with ~10pt font height.
        double yTop = ThumbHeight + 4 + 3;        // = SlotRect.Bottom + 3
        double yBot = yTop + 12;
        if (p.Y < yTop || p.Y > yBot) return -1;
        for (int slot = 0; slot < seq.FrameIndices.Count; slot++)
        {
            var r = SlotRect(slot);
            if (p.X >= r.Right - 18 && p.X <= r.Right + 2) return slot;
        }
        return -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);

        // Click on the orange delay number → raise the edit request and stop.
        int delaySlot = DelayHitAt(p);
        if (delaySlot >= 0)
        {
            var seq1 = CurrentSequence();
            if (seq1 != null)
            {
                int poolIdx1 = seq1.FrameIndices[delaySlot];
                DelayEditRequested?.Invoke(this,
                    new DelayEditEventArgs(poolIdx1, _ws.Frames[poolIdx1].DelayTime));
            }
            return;
        }

        int slot = SlotAt(p);
        if (slot < 0) return;

        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var seq = CurrentSequence()!;
        int poolIdx = seq.FrameIndices[slot];

        if (shift)
        {
            int a = _ws.SelectedFrame, b = poolIdx;
            _session.MovieLoop = (Math.Min(a, b), Math.Max(a, b));
        }
        else
        {
            _session.MovieLoop = null;
            _ws.SelectedFrame = poolIdx;
            _ws.SelectedSequence = SequenceIndex;
            _scrollCursor = slot;  // sync cursor to the clicked slot
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();

        _pressSlot = slot;
        _pressPoint = p;
        _dragReported = false;
        // DO NOT capture — we want pointer events to flow globally once the host takes over.
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pressSlot < 0 || _dragReported) return;
        var p = e.GetPosition(this);
        var d = p - _pressPoint;
        if (Math.Abs(d.X) > 5 || Math.Abs(d.Y) > 5)
        {
            _dragReported = true;
            var seq = CurrentSequence();
            if (seq == null) { _pressSlot = -1; return; }
            int poolIdx = seq.FrameIndices[_pressSlot];
            DragStarted?.Invoke(this, new FrameDragBeginEventArgs(
                sourceStrip: this,
                sourceSequenceIndex: SequenceIndex,
                sourceSlot: _pressSlot,
                poolFrameIndex: poolIdx,
                pressScreenPoint: e.GetPosition(TopLevel.GetTopLevel(this))));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _pressSlot = -1;
        _dragReported = false;
    }

    /// <summary>
    /// Mouse wheel over this strip steps the SelectedFrame to the next/previous slot
    /// in this sequence's FrameIndices. Wrap-around at the ends. Honors repeated
    /// indices by tracking the cursor as a slot position rather than re-looking it up.
    /// </summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var seq = CurrentSequence();
        if (seq == null || seq.FrameIndices.Count == 0) return;

        // Positive Delta.Y = scroll up / away from user → previous frame.
        // Negative = scroll down / toward user → next frame.
        int step = e.Delta.Y > 0 ? -1 : (e.Delta.Y < 0 ? +1 : 0);
        if (step == 0) return;

        // If the cursor hasn't been set yet, seed it from the current SelectedFrame.
        int n = seq.FrameIndices.Count;
        if (_scrollCursor < 0 || _scrollCursor >= n)
        {
            _scrollCursor = seq.FrameIndices.IndexOf(_ws.SelectedFrame);
            if (_scrollCursor < 0) _scrollCursor = 0;
        }

        _scrollCursor = ((_scrollCursor + step) % n + n) % n;
        _ws.SelectedFrame = seq.FrameIndices[_scrollCursor];
        _ws.SelectedSequence = SequenceIndex;
        e.Handled = true;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }
}

/// <summary>Raised when the user clicks the orange delay number.</summary>
public sealed class DelayEditEventArgs : EventArgs
{
    public int PoolFrameIndex { get; }
    public int CurrentDelay { get; }
    public DelayEditEventArgs(int poolFrameIndex, int currentDelay)
    {
        PoolFrameIndex = poolFrameIndex;
        CurrentDelay = currentDelay;
    }
}

/// <summary>Raised when a drag begins — host (MainWindow) takes over from here.</summary>
public sealed class FrameDragBeginEventArgs : EventArgs
{
    public FrameStrip SourceStrip { get; }
    public int SourceSequenceIndex { get; }
    public int SourceSlot { get; }
    public int PoolFrameIndex { get; }
    public Point PressScreenPoint { get; }

    public FrameDragBeginEventArgs(FrameStrip sourceStrip, int sourceSequenceIndex, int sourceSlot,
                                    int poolFrameIndex, Point pressScreenPoint)
    {
        SourceStrip = sourceStrip;
        SourceSequenceIndex = sourceSequenceIndex;
        SourceSlot = sourceSlot;
        PoolFrameIndex = poolFrameIndex;
        PressScreenPoint = pressScreenPoint;
    }
}
