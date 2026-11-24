using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SpriteEditXL.Model;

namespace SpriteEditXL.UI;

/// <summary>
/// DLI per-row color picker strip. Each row shows back/c0/c1/c2/c3 swatches,
/// each swatch 4px wide (same as the web version). No individual swatch borders —
/// rows are separated by a thin black 1px line and the selected row gets a white
/// top/bottom bar highlight.
/// Row height matches the SpriteCanvas cell height so swatches line up with sprite rows.
/// Click a row to set SelectedDli. Shift-click to set DliRange for batch edits.
/// Double-click a swatch to open the palette picker for that DLI cell.
/// </summary>
public sealed class DliPanel : Control
{
    private Workspace _ws = new();
    private Options _opt = new();
    private EditorSession _session = new();

    // Doubled from the web version's 4px for legibility. Swatch count varies by mode:
    // 5 (BG, C0, C1, C2, C3) when P2/P3 are available, otherwise 3 (BG, C0, C1).
    private const double SwatchWidth = 8;

    public event EventHandler? SelectionChanged;
    public event EventHandler? ColorEdited;

    private int ActiveSwatchCount =>
        (_opt.MergeMode.IsPlayer23Mode() || _opt.Freestyle) ? 5 : 3;

    private double PanelWidthForMode => SwatchWidth * ActiveSwatchCount;

    public DliPanel()
    {
        // Initial width (updated in SetState once opts are known).
        Width = SwatchWidth * 5;
    }

    /// <summary>Row height = sprite canvas cell height at current zoom + line resolution.</summary>
    private double RowHeight
    {
        get
        {
            int cx = Defines.ZoomCellSize[Defines.Clamp(_opt.CellSize, 0, Defines.ZoomCellSize.Length - 1)];
            return Math.Max(1, cx / Math.Max(1, _opt.LineResolution));
        }
    }

    public void SetState(Workspace ws, Options opt, EditorSession session)
    {
        _ws = ws; _opt = opt; _session = session;
        Width = PanelWidthForMode;
        Height = _opt.SpriteHeight * RowHeight;
        IsVisible = _opt.DliOn;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        if (!_opt.DliOn || _ws.Frames.Count == 0) return;
        var dli = _ws.Frames[_ws.SelectedFrame].Dli;
        int swatchCount = ActiveSwatchCount;
        double panelW = PanelWidthForMode;
        double rh = RowHeight;

        // Pre-brush the inter-row separator (black 1px bottom line, matches CSS).
        var rowSepPen = new Pen(Brushes.Black, 1);

        for (int r = 0; r < _opt.SpriteHeight; r++)
        {
            double y = r * rh;
            // Swatch slots match the number of visible color channels in this mode.
            byte[] cvals = swatchCount == 5
                ? new[] { dli.Back[r], dli.C0[r], dli.C1[r], dli.C2[r], dli.C3[r] }
                : new[] { dli.Back[r], dli.C0[r], dli.C1[r] };

            for (int i = 0; i < swatchCount; i++)
            {
                var rect = new Rect(i * SwatchWidth, y, SwatchWidth, rh);
                ctx.FillRectangle(AtariPalette.ToRgb(cvals[i], _opt.Palette).ToBrush(), rect);
            }

            // Inter-row 1px black separator.
            ctx.DrawLine(rowSepPen, new Point(0, y + rh - 0.5), new Point(panelW, y + rh - 0.5));
        }

        // Row highlights (selected / in-range), drawn over the swatches.
        var selPen = new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 2);
        var rangePen = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1);
        for (int r = 0; r < _opt.SpriteHeight; r++)
        {
            double y = r * rh;
            Pen? pen = null;
            if (r == _ws.SelectedDli) pen = selPen;
            else if (_session.DliRange is { } range && r >= range.First && r <= range.Last) pen = rangePen;
            if (pen == null) continue;

            ctx.DrawLine(pen, new Point(0, y + pen.Thickness / 2),
                              new Point(panelW, y + pen.Thickness / 2));
            ctx.DrawLine(pen, new Point(0, y + rh - pen.Thickness / 2),
                              new Point(panelW, y + rh - pen.Thickness / 2));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        int row = (int)(p.Y / RowHeight);
        if (row < 0 || row >= _opt.SpriteHeight) return;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (shift)
        {
            int a = _ws.SelectedDli, b = row;
            _session.DliRange = (Math.Min(a, b), Math.Max(a, b));
        }
        else
        {
            _session.DliRange = null;
            _ws.SelectedDli = row;
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();

        if (e.ClickCount == 2) OpenPickerFor(row, (int)(p.X / SwatchWidth));
    }

    private async void OpenPickerFor(int row, int slot)
    {
        if (slot < 0 || slot >= ActiveSwatchCount) return;
        var dli = _ws.Frames[_ws.SelectedFrame].Dli;
        byte[] arr = slot switch { 1 => dli.C0, 2 => dli.C1, 3 => dli.C2, 4 => dli.C3, _ => dli.Back };
        var win = TopLevel.GetTopLevel(this) as Window;
        if (win is null) return;
        var dlg = new PaletteDialog(_opt.Palette, arr[row]);
        var picked = await dlg.ShowDialog<byte?>(win);
        if (picked is byte cval)
        {
            if (_session.DliRange is { } range)
                for (int r = range.First; r <= range.Last; r++) arr[r] = cval;
            else arr[row] = cval;
            ColorEdited?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }
}
