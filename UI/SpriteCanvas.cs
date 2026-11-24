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
/// Sprite pixel editor rendered through a WriteableBitmap for maximum fidelity.
/// Pointer input: Left = paint SelectedColor, Right/Shift+Left = paint background.
/// </summary>
public sealed class SpriteCanvas : Control
{
    private Workspace _ws = new();
    private Options _opt = new();
    private EditorSession _session = new();
    private PixelEngine _engine;
    private WriteableBitmap? _bitmap;
    private int _pixelW, _pixelH;
    private bool _penDown;
    private int _penColor;
    private int _hoverRow = -1;

    public event EventHandler? StrokeBegan;
    public event EventHandler? StrokeEnded;
    public event EventHandler? PixelChanged;
    public event EventHandler<int>? RowHoverChanged;
    public event EventHandler<int>? InsertRowRequested;
    public event EventHandler<int>? DeleteRowRequested;

    public SpriteCanvas()
    {
        _engine = new PixelEngine(_ws, _opt);
        Focusable = true;
    }

    public void SetState(Workspace ws, Options opt, EditorSession session)
    {
        _ws = ws; _opt = opt; _session = session;
        _engine = new PixelEngine(ws, opt);
        Recompute();
    }

    public void Recompute()
    {
        var size = FrameRenderer.GetGridSize(_ws, _opt);
        int cx = Defines.ZoomCellSize[Defines.Clamp(_opt.CellSize, 0, Defines.ZoomCellSize.Length - 1)];
        int cy = Math.Max(1, cx / _opt.LineResolution);
        if (!_opt.SquarePixel) cx = (int)(cx * 1.2);
        _pixelW = size.Cols * cx;
        _pixelH = size.Rows * cy;
        if (_pixelW <= 0 || _pixelH <= 0) return;

        _bitmap?.Dispose();
        _bitmap = new WriteableBitmap(new PixelSize(_pixelW, _pixelH), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Opaque);
        Width = _pixelW;
        Height = _pixelH;
        RedrawBitmap();
        InvalidateVisual();
    }

    public (int cellW, int cellH) CellPixelSize()
    {
        int cx = Defines.ZoomCellSize[Defines.Clamp(_opt.CellSize, 0, Defines.ZoomCellSize.Length - 1)];
        int cy = Math.Max(1, cx / _opt.LineResolution);
        if (!_opt.SquarePixel) cx = (int)(cx * 1.2);
        return (cx, cy);
    }

    private void RedrawBitmap()
    {
        if (_bitmap is null) return;
        var (cw, ch) = CellPixelSize();
        var data = FrameRenderer.RenderBgra(_ws, _opt, _session, _ws.SelectedFrame, cw, ch, out int w, out int h);
        using var fb = _bitmap.Lock();
        unsafe
        {
            fixed (byte* src = data)
            {
                Buffer.MemoryCopy(src, (void*)fb.Address, data.Length, data.Length);
            }
        }
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        if (_bitmap != null)
            ctx.DrawImage(_bitmap, new Rect(0, 0, _pixelW, _pixelH));

        var (cw, ch) = CellPixelSize();
        var size = FrameRenderer.GetGridSize(_ws, _opt);

        if (_opt.ShowGrid > 0)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(77, 200, 200, 200)));
            for (int c = 1; c < size.Cols; c++)
                ctx.DrawLine(gridPen, new Point(c * cw, 0), new Point(c * cw, _pixelH));
            for (int r = 1; r < size.Rows; r++)
                ctx.DrawLine(gridPen, new Point(0, r * ch), new Point(_pixelW, r * ch));
        }

        if (_opt.MarkActiveRegion) DrawActiveRegionMarker(ctx, cw, ch, size);

        if (_session.AltPressed && _hoverRow >= 0 && _hoverRow < _opt.SpriteHeight)
            DrawRowGutter(ctx, cw, ch, _hoverRow);
    }

    private void DrawRowGutter(DrawingContext ctx, int cw, int ch, int row)
    {
        double y = (row + _opt.BackOffsetV) * ch + ch / 2.0;
        var font = new Typeface("Inter");
        var plusBrush = new SolidColorBrush(Color.FromArgb(220, 60, 180, 60));
        var minusBrush = new SolidColorBrush(Color.FromArgb(220, 180, 60, 60));
        var plusRect = new Rect(-36, y - 14, 16, 16);
        var minusRect = new Rect(-18, y - 14, 16, 16);
        ctx.FillRectangle(plusBrush, plusRect);
        ctx.FillRectangle(minusBrush, minusRect);
        var txtP = new FormattedText("+", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, font, 14, Brushes.White);
        var txtM = new FormattedText("−", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, font, 14, Brushes.White);
        ctx.DrawText(txtP, new Point(plusRect.X + 3, plusRect.Y - 2));
        ctx.DrawText(txtM, new Point(minusRect.X + 3, minusRect.Y - 2));
    }

    private void DrawActiveRegionMarker(DrawingContext ctx, int cw, int ch, FrameRenderer.GridSize size)
    {
        int active = _ws.SelectedColor;
        (int startCol, int width)? span = GetMarkerSpan(active);
        if (!span.HasValue) return;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 255, 0)), 2);
        int sx = (span.Value.startCol + _opt.BackOffsetH) * cw;
        ctx.DrawRectangle(null, pen, new Rect(sx, 0, span.Value.width * cw, size.Rows * ch));
    }

    private (int startCol, int width)? GetMarkerSpan(int c)
    {
        int width = Defines.SpriteWidthPerMode[(int)_opt.MergeMode];
        int cell;
        switch (c)
        {
            case 1: cell = 0; break;
            case 2: cell = _opt.SpriteGap01; break;
            case 3:
                if (_opt.OrDrawsOutside) { width += _opt.SpriteGap01; cell = 0; }
                else { width -= _opt.SpriteGap01; cell = _opt.SpriteGap01; }
                break;
            case 5 when _opt.MergeMode.IsPlayer23Mode(): cell = _opt.PairGap; break;
            case 6 when _opt.MergeMode.IsPlayer23Mode(): cell = _opt.PairGap + _opt.SpriteGap23; break;
            case 7 when _opt.MergeMode.IsPlayer23Mode():
                if (_opt.OrDrawsOutside) { width += _opt.SpriteGap23; cell = _opt.PairGap; }
                else { width -= _opt.SpriteGap23; cell = _opt.PairGap + _opt.SpriteGap23; }
                break;
            default: return null;
        }
        return (cell, width);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_ws.Frames.Count == 0) return;
        Focus();
        var pt = e.GetCurrentPoint(this);

        // Row gutter click: when Alt held, check for +/- hit zones in the negative-X area
        if (_session.AltPressed && _hoverRow >= 0)
        {
            double y = pt.Position.Y;
            var (cw, ch) = CellPixelSize();
            double rowMidY = (_hoverRow + _opt.BackOffsetV) * ch + ch / 2.0;
            if (y >= rowMidY - 14 && y <= rowMidY + 2)
            {
                if (pt.Position.X < -20 && pt.Position.X >= -36) { InsertRowRequested?.Invoke(this, _hoverRow); return; }
                if (pt.Position.X < -2  && pt.Position.X >= -18) { DeleteRowRequested?.Invoke(this, _hoverRow); return; }
            }
        }

        // Painting is blocked during playback unless the user opted into "Draw on animation".
        if (_session.AnimationOn && !_opt.DrawOnPlay) return;

        _penDown = true;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        _penColor = (pt.Properties.IsRightButtonPressed || shift) ? 0 : _ws.SelectedColor;
        StrokeBegan?.Invoke(this, EventArgs.Empty);
        PaintAt(pt.Position);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var (cw, ch) = CellPixelSize();
        int row = (int)(pos.Y / ch) - _opt.BackOffsetV;
        if (row >= 0 && row < _opt.SpriteHeight)
        {
            if (_hoverRow != row) { _hoverRow = row; if (_session.AltPressed) InvalidateVisual(); }
            RowHoverChanged?.Invoke(this, row);
        }
        if (_penDown) PaintAt(pos);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_penDown) return;
        _penDown = false;
        e.Pointer.Capture(null);
        StrokeEnded?.Invoke(this, EventArgs.Empty);
    }

    private void PaintAt(Point p)
    {
        var (cw, ch) = CellPixelSize();
        int col = (int)(p.X / cw) - _opt.BackOffsetH;
        int row = (int)(p.Y / ch) - _opt.BackOffsetV;
        if (col < 0 || col >= _engine.ColumnsActive) return;
        if (row < 0 || row >= _opt.SpriteHeight) return;

        var (first, last) = _session.EffectiveRange(_ws.SelectedFrame, _opt.MultiFrameEdit);
        bool changed = false;
        for (int f = first; f <= last; f++)
        {
            int before = _engine.GetColorOn(f, col, row, _session.Layer01Visible, _session.Layer23Visible);
            _engine.SetColorOn(f, col, row, _penColor, _session.Layer01Visible, _session.Layer23Visible);
            if (_opt.LinkColors && _penColor > 0)
            {
                int linked = _penColor < 4 ? _penColor + 4 : _penColor - 4;
                _engine.SetColorOn(f, col, row, linked, _session.Layer01Visible, _session.Layer23Visible);
            }
            int after = _engine.GetColorOn(f, col, row, _session.Layer01Visible, _session.Layer23Visible);
            if (after != before) changed = true;
        }
        if (changed)
        {
            RedrawBitmap();
            InvalidateVisual();
            PixelChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Force a full redraw (after transforms, frame change, etc.).</summary>
    public void Refresh()
    {
        RedrawBitmap();
        InvalidateVisual();
    }
}
