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
/// Mini-map of the full reference image, with a yellow outline showing the source
/// region currently being sampled into the editor. Dragging the yellow box pans
/// the reference image (updates <see cref="Options.ReferenceSourceX"/>/<c>Y</c>).
/// </summary>
public sealed class ReferenceMinimap : Control
{
    private Workspace _ws = new();
    private Options _opt = new();

    /// <summary>Raised when the user drags the yellow box, after options have been updated.</summary>
    public event EventHandler? PanChanged;

    private const double MinimapMaxWidth = 200;
    private const double MinimapMaxHeight = 200;

    private WriteableBitmap? _thumbBitmap;
    private double _thumbScale = 1;
    private bool _dragging;
    private Point _dragStartImgOffset;
    private Point _dragStartPointer;

    public ReferenceMinimap()
    {
        Width = MinimapMaxWidth;
        Height = 40;  // placeholder until an image is loaded
    }

    public void SetState(Workspace ws, Options opt)
    {
        _ws = ws;
        _opt = opt;
        RebuildThumbnail();
        InvalidateVisual();
    }

    private void RebuildThumbnail()
    {
        _thumbBitmap?.Dispose();
        _thumbBitmap = null;

        if (_ws.ReferenceImage == null || _ws.ReferenceImageWidth <= 0 || _ws.ReferenceImageHeight <= 0)
        {
            Width = MinimapMaxWidth;
            Height = 40;
            return;
        }

        int srcW = _ws.ReferenceImageWidth;
        int srcH = _ws.ReferenceImageHeight;
        double scale = Math.Min(MinimapMaxWidth / srcW, MinimapMaxHeight / srcH);
        if (scale > 1) scale = 1; // never magnify in the minimap
        _thumbScale = scale;

        int thumbW = Math.Max(1, (int)Math.Round(srcW * scale));
        int thumbH = Math.Max(1, (int)Math.Round(srcH * scale));

        // Resample into a WriteableBitmap sized thumbW x thumbH.
        _thumbBitmap = new WriteableBitmap(
            new PixelSize(thumbW, thumbH),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        var buf = new byte[thumbW * thumbH * 4];
        var src = _ws.ReferenceImage;
        // Nearest-neighbor downsample: good enough for a tiny preview.
        for (int ty = 0; ty < thumbH; ty++)
        {
            int sy = Math.Min(srcH - 1, (int)(ty / scale));
            for (int tx = 0; tx < thumbW; tx++)
            {
                int sx = Math.Min(srcW - 1, (int)(tx / scale));
                int srcOfs = (sy * srcW + sx) * 4;
                int dstOfs = (ty * thumbW + tx) * 4;
                buf[dstOfs + 0] = src[srcOfs + 0];
                buf[dstOfs + 1] = src[srcOfs + 1];
                buf[dstOfs + 2] = src[srcOfs + 2];
                buf[dstOfs + 3] = 255;
            }
        }

        using (var fb = _thumbBitmap.Lock())
        {
            unsafe
            {
                fixed (byte* bp = buf)
                    Buffer.MemoryCopy(bp, (void*)fb.Address, buf.Length, buf.Length);
            }
        }

        Width = thumbW;
        Height = thumbH;
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        if (_thumbBitmap == null)
        {
            // Placeholder when no reference loaded.
            var font = new Typeface("Inter");
            ctx.DrawText(new FormattedText("(no reference)",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, font, 11,
                new SolidColorBrush(Color.FromArgb(200, 164, 190, 224))),
                new Point(4, 10));
            return;
        }

        ctx.DrawImage(_thumbBitmap,
            new Rect(0, 0, _thumbBitmap.PixelSize.Width, _thumbBitmap.PixelSize.Height));

        DrawViewport(ctx);
    }

    private void DrawViewport(DrawingContext ctx)
    {
        var size = FrameRenderer.GetGridSize(_ws, _opt);
        int viewportSrcW = size.Cols * Math.Max(1, _opt.ReferenceScaleX);
        int viewportSrcH = size.Rows * Math.Max(1, _opt.ReferenceScaleY);
        double x = _opt.ReferenceSourceX * _thumbScale;
        double y = _opt.ReferenceSourceY * _thumbScale;
        double w = viewportSrcW * _thumbScale;
        double h = viewportSrcH * _thumbScale;
        var pen = new Pen(Brushes.Yellow, 1.5);
        ctx.DrawRectangle(null, pen, new Rect(x, y, w, h));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_thumbBitmap == null) return;
        var p = e.GetPosition(this);
        _dragging = true;
        _dragStartPointer = p;
        _dragStartImgOffset = new Point(_opt.ReferenceSourceX, _opt.ReferenceSourceY);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || _thumbBitmap == null) return;
        var p = e.GetPosition(this);
        double dx = (p.X - _dragStartPointer.X) / _thumbScale;
        double dy = (p.Y - _dragStartPointer.Y) / _thumbScale;

        var size = FrameRenderer.GetGridSize(_ws, _opt);
        int viewportSrcW = size.Cols * Math.Max(1, _opt.ReferenceScaleX);
        int viewportSrcH = size.Rows * Math.Max(1, _opt.ReferenceScaleY);
        int maxX = Math.Max(0, _ws.ReferenceImageWidth - viewportSrcW);
        int maxY = Math.Max(0, _ws.ReferenceImageHeight - viewportSrcH);

        _opt.ReferenceSourceX = Math.Clamp((int)Math.Round(_dragStartImgOffset.X + dx), 0, maxX);
        _opt.ReferenceSourceY = Math.Clamp((int)Math.Round(_dragStartImgOffset.Y + dy), 0, maxY);
        PanChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
        }
    }
}
