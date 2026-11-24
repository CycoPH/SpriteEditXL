using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SpriteEditXL.Model;

namespace SpriteEditXL.UI;

/// <summary>Modal 16×16 Atari color picker. Returns byte? (selected color, or null if cancelled).</summary>
public sealed class PaletteDialog : Window
{
    private const int Cell = 28;
    private readonly PaletteKind _palette;
    private byte _current;

    public PaletteDialog(PaletteKind palette, byte current)
    {
        _palette = palette;
        _current = current;
        Title = "Pick color";
        Width = Cell * 16 + 2;
        Height = Cell * 16 + 36; // room for hover-preview strip
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
    }

    private Control BuildContent()
    {
        var grid = new PaletteGridControl(_palette, _current);
        var preview = new PaletteHoverPreview(_palette, _current);
        grid.HoverChanged += (_, cval) => preview.SetHover(cval);
        grid.PointerPressed += (_, e) =>
        {
            var p = e.GetPosition(grid);
            int col = (int)(p.X / Cell);
            int row = (int)(p.Y / Cell);
            if (col < 0 || col > 15 || row < 0 || row > 15) return;
            byte picked = (byte)((row << 4) | col);
            Close((byte?)picked);
        };
        var root = new DockPanel();
        DockPanel.SetDock(preview, Dock.Bottom);
        root.Children.Add(preview);
        root.Children.Add(grid);
        return root;
    }
}

internal sealed class PaletteGridControl : Control
{
    private readonly PaletteKind _palette;
    private readonly byte _current;
    private const int Cell = 28;
    private int _hoverCol = -1, _hoverRow = -1;

    public event EventHandler<byte>? HoverChanged;

    public PaletteGridControl(PaletteKind p, byte current)
    {
        _palette = p; _current = current;
        Width = Cell * 16; Height = Cell * 16;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        int col = (int)(p.X / Cell);
        int row = (int)(p.Y / Cell);
        if (col < 0 || col > 15 || row < 0 || row > 15) return;
        if (col != _hoverCol || row != _hoverRow)
        {
            _hoverCol = col; _hoverRow = row;
            HoverChanged?.Invoke(this, (byte)((row << 4) | col));
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverCol = _hoverRow = -1;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        for (int row = 0; row < 16; row++)
        for (int col = 0; col < 16; col++)
        {
            byte cval = (byte)((row << 4) | col);
            var rect = new Rect(col * Cell, row * Cell, Cell, Cell);
            ctx.FillRectangle(AtariPalette.ToRgb(cval, _palette).ToBrush(), rect);
            if (cval == _current)
                ctx.DrawRectangle(null, new Pen(Brushes.White, 2), rect.Deflate(1));
            if (row == _hoverRow && col == _hoverCol)
                ctx.DrawRectangle(null, new Pen(Brushes.Yellow, 2), rect.Deflate(1));
        }
    }
}

internal sealed class PaletteHoverPreview : Control
{
    private readonly PaletteKind _palette;
    private byte _current;
    private byte _hover;

    public PaletteHoverPreview(PaletteKind p, byte current)
    {
        _palette = p; _current = current; _hover = current;
        Height = 32;
    }

    public void SetHover(byte cval)
    {
        _hover = cval;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        ctx.FillRectangle(Brushes.Black, new Rect(0, 0, Bounds.Width, Bounds.Height));
        var curBrush   = AtariPalette.ToRgb(_current, _palette).ToBrush();
        var hoverBrush = AtariPalette.ToRgb(_hover,   _palette).ToBrush();
        double half = Bounds.Width / 2.0;
        ctx.FillRectangle(curBrush,   new Rect(0,    0, half,  Bounds.Height));
        ctx.FillRectangle(hoverBrush, new Rect(half, 0, half,  Bounds.Height));
        var font = new Typeface("Inter", FontStyle.Normal, FontWeight.Bold);
        var text = $"current  ${_current:X2}   →   hover  ${_hover:X2}";
        var label = new FormattedText(text,
            System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            font, 12, Brushes.White);
        var pt = new Point((Bounds.Width - label.Width) / 2, (Bounds.Height - label.Height) / 2);
        // soft shadow for legibility over any hue
        foreach (var dx in new[] { -1, 0, 1 })
        foreach (var dy in new[] { -1, 0, 1 })
            if (dx != 0 || dy != 0)
            {
                var halo = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, font, 12, Brushes.Black);
                ctx.DrawText(halo, pt + new Vector(dx, dy));
            }
        ctx.DrawText(label, pt);
    }
}
