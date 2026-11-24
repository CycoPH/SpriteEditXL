using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SpriteEditXL.Model;

namespace SpriteEditXL.UI;

/// <summary>
/// Vertical color stack matching the original SprEd layout:
///   row 0: BACKGND (wide)
///   row 1: C0 | C1  (two narrow, side-by-side)
///   row 2: C0 OR C1 (wide)
///   row 3: C2 | C3  (two narrow, when P2+P3 mode)
///   row 4: C2 OR C3 (wide, when P2+P3 mode)
/// Plus two framepreview thumbnails beneath.
/// Left click: select. Double click: palette picker. Right click: toggle sprite visibility.
/// </summary>
public sealed class ColorStrip : Control
{
    private Workspace _ws = new();
    private Options _opt = new();
    private EditorSession _session = new();

    private const double WideWidth    = 132;
    private const double NarrowWidth  = 61;
    private const double NarrowGap    = 7;
    private const double SlotHeight   = 24;
    private const double RowSpacing   = 4;
    private const double PreviewPad   = 8;
    private const double SwatchSize   = 16;     // picker swatch

    public event EventHandler? SelectionChanged;
    public event EventHandler? ColorEdited;
    public event EventHandler? VisibilityChanged;
    public event EventHandler? LinkColorsToggled;

    public ColorStrip()
    {
        Width = WideWidth + 16;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Natural height of the color stack + two framepreview thumbnails.
        bool p23 = _opt.MergeMode.IsPlayer23Mode() || _opt.Freestyle;
        int rows = p23 ? 5 : 3;
        double stackHeight = rows * (SlotHeight + RowSpacing);

        // Approximate preview block height (scale 3 then scale 6 stacked)
        var size = FrameRenderer.GetGridSize(_ws, _opt);
        double preview1H = size.Rows * Math.Max(1, 3 / Math.Max(1, _opt.LineResolution));
        double preview2H = size.Rows * Math.Max(1, 6 / Math.Max(1, _opt.LineResolution));
        double previewsHeight = preview1H + PreviewPad + preview2H + PreviewPad;

        return new Size(Width, stackHeight + previewsHeight);
    }

    public void SetState(Workspace ws, Options opt, EditorSession session)
    {
        _ws = ws; _opt = opt; _session = session;
        InvalidateVisual();
    }

    // Slot index -> color index into engine.GetColors() palette
    // 0=BG, 1=C0, 2=C1, 3=C0|C1, 5=C2, 6=C3, 7=C2|C3

    /// <summary>Hit-test for the whole slot. Returns -1 if outside any slot.</summary>
    private int SlotAt(Point p)
    {
        bool p23 = _opt.MergeMode.IsPlayer23Mode() || _opt.Freestyle;
        double y = 0;

        // Row 0: BACKGND
        if (In(p, 0, y, WideWidth, SlotHeight)) return 0;
        y += SlotHeight + RowSpacing;

        // Row 1: C0 | C1
        if (In(p, 0, y, NarrowWidth, SlotHeight)) return 1;
        if (In(p, NarrowWidth + NarrowGap, y, NarrowWidth, SlotHeight)) return 2;
        y += SlotHeight + RowSpacing;

        // Row 2: C0 OR C1
        if (In(p, 0, y, WideWidth, SlotHeight)) return 3;
        y += SlotHeight + RowSpacing;

        if (p23)
        {
            if (In(p, 0, y, NarrowWidth, SlotHeight)) return 5;
            if (In(p, NarrowWidth + NarrowGap, y, NarrowWidth, SlotHeight)) return 6;
            y += SlotHeight + RowSpacing;

            if (In(p, 0, y, WideWidth, SlotHeight)) return 7;
        }

        return -1;
    }

    /// <summary>
    /// Hit-test for the rainbow picker swatch on the right side of each slot.
    /// Returns -1 if the click wasn't on a swatch. OR slots (3,7) have no swatch.
    /// </summary>
    private int SwatchAt(Point p)
    {
        bool p23 = _opt.MergeMode.IsPlayer23Mode() || _opt.Freestyle;
        double y = 0;

        if (InSwatch(p, 0, y, WideWidth))   return 0;
        y += SlotHeight + RowSpacing;

        if (InSwatch(p, 0, y, NarrowWidth)) return 1;
        if (InSwatch(p, NarrowWidth + NarrowGap, y, NarrowWidth)) return 2;
        y += SlotHeight + RowSpacing;

        // Row 2 (C0 OR C1): no swatch
        y += SlotHeight + RowSpacing;

        if (p23)
        {
            if (InSwatch(p, 0, y, NarrowWidth)) return 5;
            if (InSwatch(p, NarrowWidth + NarrowGap, y, NarrowWidth)) return 6;
            // Row 4 (C2 OR C3): no swatch
        }

        return -1;
    }

    /// <summary>True if <paramref name="p"/> lies inside the swatch of a slot at (x,y) with width w.</summary>
    private static bool InSwatch(Point p, double x, double y, double w)
    {
        double swY = y + (SlotHeight - SwatchSize + 2) / 2;
        var sw = new Rect(x + w - SwatchSize - 6, swY, SwatchSize, SwatchSize - 2);
        return sw.Contains(p);
    }

    private static bool In(Point p, double x, double y, double w, double h) =>
        p.X >= x && p.X <= x + w && p.Y >= y && p.Y <= y + h;

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        if (_ws.Frames.Count == 0) return;
        var engine = new PixelEngine(_ws, _opt);
        var colors = engine.GetColors(_ws.SelectedFrame, _ws.SelectedDli);
        bool p23 = _opt.MergeMode.IsPlayer23Mode() || _opt.Freestyle;
        double y = 0;

        DrawSlot(ctx, 0, 0, y, WideWidth, SlotHeight,  colors[0], "BACKGND", -1);
        y += SlotHeight + RowSpacing;

        DrawSlot(ctx, 1, 0,                      y, NarrowWidth, SlotHeight, colors[1], "C0", 0);
        DrawSlot(ctx, 2, NarrowWidth + NarrowGap,y, NarrowWidth, SlotHeight, colors[2], "C1", 1);
        DrawLinkGlyph(ctx, NarrowWidth + (NarrowGap - 14) / 2.0, y + (SlotHeight - 14) / 2.0);
        y += SlotHeight + RowSpacing;

        DrawSlot(ctx, 3, 0, y, WideWidth, SlotHeight, colors[3], "C0 OR C1", -1);
        y += SlotHeight + RowSpacing;

        if (p23)
        {
            DrawSlot(ctx, 5, 0,                       y, NarrowWidth, SlotHeight, colors[5], "C2", 2);
            DrawSlot(ctx, 6, NarrowWidth + NarrowGap, y, NarrowWidth, SlotHeight, colors[6], "C3", 3);
            DrawLinkGlyph(ctx, NarrowWidth + (NarrowGap - 14) / 2.0, y + (SlotHeight - 14) / 2.0);
            y += SlotHeight + RowSpacing;

            DrawSlot(ctx, 7, 0, y, WideWidth, SlotHeight, colors[7], "C2 OR C3", -1);
            y += SlotHeight + RowSpacing;
        }

        // Frame preview thumbnails (two of them, small/medium)
        y += PreviewPad;
        DrawFramePreview(ctx, 0, y, 3);
        DrawFramePreview(ctx, 60, y, 3);
        y += PreviewRowHeight(3) + PreviewPad;
        DrawFramePreview(ctx, 0, y, 6);
    }

    private double PreviewRowHeight(int scale)
    {
        var size = FrameRenderer.GetGridSize(_ws, _opt);
        int ch = Math.Max(1, scale / Math.Max(1, _opt.LineResolution));
        return size.Rows * ch;
    }

    private void DrawFramePreview(DrawingContext ctx, double x, double y, int scale)
    {
        var size = FrameRenderer.GetGridSize(_ws, _opt);
        int cw = scale;
        int ch = Math.Max(1, scale / Math.Max(1, _opt.LineResolution));
        var engine = new PixelEngine(_ws, _opt);
        for (int r = 0; r < size.Rows; r++)
        {
            int dataRow = r - _opt.BackOffsetV;
            var cols = engine.GetColors(_ws.SelectedFrame, dataRow);
            for (int c = 0; c < size.Cols; c++)
            {
                int dataCol = c - _opt.BackOffsetH;
                byte cval;
                if (dataRow >= 0 && dataRow < _opt.SpriteHeight &&
                    dataCol >= 0 && dataCol < engine.ColumnsActive)
                {
                    int ci = engine.GetColorOn(_ws.SelectedFrame, dataCol, dataRow, true, true);
                    cval = cols[ci];
                }
                else cval = _ws.BackgroundColor;
                ctx.FillRectangle(AtariPalette.ToRgb(cval, _opt.Palette).ToBrush(),
                                  new Rect(x + c * cw, y + r * ch, cw, ch));
            }
        }
    }

    private void DrawSlot(DrawingContext ctx, int slot, double x, double y, double w, double h,
                          byte cval, string label, int playerSlot)
    {
        var rect = new Rect(x, y, w, h);
        var brush = AtariPalette.ToRgb(cval, _opt.Palette).ToBrush();
        ctx.DrawRectangle(brush, null, rect, 3, 3);

        // Classic 3D button border
        var topLeft  = new Pen(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), 1);
        var botRight = new Pen(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), 1);
        ctx.DrawLine(topLeft,  new Point(rect.X, rect.Y),              new Point(rect.Right - 1, rect.Y));
        ctx.DrawLine(topLeft,  new Point(rect.X, rect.Y),              new Point(rect.X, rect.Bottom - 1));
        ctx.DrawLine(botRight, new Point(rect.X, rect.Bottom - 1),     new Point(rect.Right - 1, rect.Bottom - 1));
        ctx.DrawLine(botRight, new Point(rect.Right - 1, rect.Y),      new Point(rect.Right - 1, rect.Bottom - 1));

        if (slot == _ws.SelectedColor)
        {
            var pen = new Pen(Brushes.White, 2);
            ctx.DrawRectangle(null, pen, rect.Deflate(1), 3, 3);
        }

        // Label (hidden sprite gets crossed). Pick fg based on swatch luma so it's always legible.
        bool hidden = playerSlot >= 0 && _ws.HiddenSprites[playerSlot];
        var font = new Typeface("Arial", FontStyle.Normal, FontWeight.Black);
        var rgb = AtariPalette.ToRgb(cval, _opt.Palette);
        // Rec. 601 luma — matches perceptual brightness well enough for a swatch text.
        double luma = 0.299 * rgb.R + 0.587 * rgb.G + 0.114 * rgb.B;
        var fgBrush = luma < 140 ? Brushes.White : Brushes.Black;
        var haloBrush = luma < 140 ? Brushes.Black : Brushes.White;
        var text = hidden ? $"{label} ×" : label;

        // Soft 1-px halo so the label survives a swatch that's close to mid-gray.
        var halo = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, font, 11, haloBrush);
        var origin = new Point(rect.X + 10, rect.Y + (h - halo.Height) / 2);
        foreach (var dx in new[] { -1, 0, 1 })
            foreach (var dy in new[] { -1, 0, 1 })
                if (dx != 0 || dy != 0)
                    ctx.DrawText(halo, origin + new Vector(dx, dy));

        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, font, 11, fgBrush);
        ctx.DrawText(ft, origin);

        // Palette-picker swatch on the right (except for OR rows)
        if (slot != 3 && slot != 7)
        {
            var swatchRect = new Rect(rect.Right - SwatchSize - 6, rect.Y + (h - SwatchSize + 2) / 2, SwatchSize, SwatchSize - 2);
            var rainbow = BuildRainbowBrush();
            ctx.DrawRectangle(rainbow, new Pen(Brushes.Black, 1), swatchRect, 2, 2);
        }
    }

    /// <summary>
    /// Returns the two link-glyph hit rects (one between C0/C1, optionally one between C2/C3).
    /// Used for hit-testing and drawing so both stay in sync.
    /// </summary>
    private IEnumerable<Rect> LinkGlyphRects()
    {
        double x = NarrowWidth + (NarrowGap - 14) / 2.0;
        double y0 = SlotHeight + RowSpacing;
        yield return new Rect(x, y0 + (SlotHeight - 14) / 2.0, 14, 14);
        bool p23 = _opt.MergeMode.IsPlayer23Mode() || _opt.Freestyle;
        if (p23)
        {
            double y2 = 3 * (SlotHeight + RowSpacing);
            yield return new Rect(x, y2 + (SlotHeight - 14) / 2.0, 14, 14);
        }
    }

    private void DrawLinkGlyph(DrawingContext ctx, double x, double y)
    {
        var on = _opt.LinkColors;
        var fill = on ? new SolidColorBrush(Color.FromRgb(0xF4, 0xD4, 0x7A))
                      : new SolidColorBrush(Color.FromArgb(128, 170, 170, 170));
        var stroke = new Pen(Brushes.Black, 1);
        // Two chain links as rounded rectangles with a gap in the middle.
        ctx.DrawRectangle(fill, stroke, new Rect(x,     y + 2, 6, 10), 2, 2);
        ctx.DrawRectangle(fill, stroke, new Rect(x + 8, y + 2, 6, 10), 2, 2);
        ctx.DrawLine(new Pen(Brushes.Black, 1), new Point(x + 5, y + 7), new Point(x + 9, y + 7));
    }

    private static LinearGradientBrush BuildRainbowBrush()
    {
        var b = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.Red,     0.00),
                new GradientStop(Colors.Yellow,  0.20),
                new GradientStop(Colors.Lime,    0.40),
                new GradientStop(Colors.Cyan,    0.60),
                new GradientStop(Colors.Magenta, 0.80),
                new GradientStop(Colors.Red,     1.00)
            }
        };
        return b;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetCurrentPoint(this);

        // Click a chain glyph between a color pair → toggle LinkColors.
        foreach (var r in LinkGlyphRects())
        {
            if (r.Contains(pt.Position))
            {
                _opt.LinkColors = !_opt.LinkColors;
                LinkColorsToggled?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
                return;
            }
        }

        // Rainbow picker swatch: single-click opens the palette (matches SprEd .picker).
        int swatchSlot = SwatchAt(pt.Position);
        if (swatchSlot >= 0 && pt.Properties.IsLeftButtonPressed)
        {
            _ws.SelectedColor = swatchSlot;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            OpenPicker(swatchSlot);
            return;
        }

        int slot = SlotAt(pt.Position);
        if (slot < 0) return;

        if (pt.Properties.IsRightButtonPressed)
        {
            int p = slot switch { 1 => 0, 2 => 1, 5 => 2, 6 => 3, _ => -1 };
            if (p >= 0)
            {
                _ws.HiddenSprites[p] = !_ws.HiddenSprites[p];
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
            }
            return;
        }

        _ws.SelectedColor = slot;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();

        if (e.ClickCount == 2) OpenPicker(slot);
    }

    private async void OpenPicker(int slot)
    {
        if (slot == 3 || slot == 7) return;  // OR slots not editable
        var engine = new PixelEngine(_ws, _opt);
        var cur = engine.GetColors(_ws.SelectedFrame, _ws.SelectedDli)[slot];
        var win = TopLevel.GetTopLevel(this) as Window;
        if (win is null) return;
        var dlg = new PaletteDialog(_opt.Palette, cur);
        var picked = await dlg.ShowDialog<byte?>(win);
        if (picked is byte cval)
        {
            SetSlot(slot, cval);
            ColorEdited?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    private void SetSlot(int slot, byte cval)
    {
        var (first, last) = _session.EffectiveRange(_ws.SelectedFrame, _opt.MultiFrameEdit);
        int[] map = { -1, 0, 1, -1, -1, 2, 3 };
        for (int f = first; f <= last; f++)
        {
            if (_opt.DliOn && slot != 0 && slot <= 6)
            {
                var dliName = Defines.DliColorMap[slot];
                if (dliName == null) continue;
                var dli = _ws.Frames[f].Dli;
                byte[] arr = dliName switch { "c0" => dli.C0, "c1" => dli.C1, "c2" => dli.C2, "c3" => dli.C3, _ => dli.Back };
                if (_session.DliRange is { } range)
                    for (int r = range.First; r <= range.Last; r++) arr[r] = cval;
                else arr[_ws.SelectedDli] = cval;
            }
            else
            {
                if (slot == 0) { _ws.BackgroundColor = cval; continue; }
                int c = slot < map.Length ? map[slot] : -1;
                if (c < 0) continue;
                int frameIdx = _opt.CommonPalette ? 0 : f;
                _ws.Frames[frameIdx].Colors[c] = cval;
            }
        }
    }
}
