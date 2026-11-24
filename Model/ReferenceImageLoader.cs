using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SpriteEditXL.Model;

/// <summary>
/// Loads an arbitrary bitmap image (PNG/GIF/JPG/BMP/anything Avalonia supports) into
/// a BGRA8888 byte buffer suitable for direct blit onto a WriteableBitmap.
/// </summary>
public static class ReferenceImageLoader
{
    /// <summary>
    /// Load the file at <paramref name="path"/> and return raw BGRA bytes + dimensions.
    /// Throws on unreadable / unsupported files.
    /// </summary>
    public static (byte[] bgra, int width, int height) Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var bmp = new Bitmap(stream);

        int w = bmp.PixelSize.Width;
        int h = bmp.PixelSize.Height;
        int stride = w * 4;
        var buf = new byte[stride * h];

        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            bmp.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), buf.Length, stride);
        }
        finally
        {
            handle.Free();
        }
        return (buf, w, h);
    }
}
