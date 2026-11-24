using System;
using System.Collections.Generic;

namespace SpriteEditXL.Model;

/// <summary>2-bpp background-image loader. Ported from parseBackImage in io.js.</summary>
public static class BackgroundImage
{
    public static byte[] Parse(byte[] bin)
    {
        if (bin.Length < 4) throw new ArgumentException("file too small");
        int colorPtr = bin.Length - 4;
        var colors = new byte[4];
        Array.Copy(bin, colorPtr, colors, 0, 4);
        var image = new byte[colorPtr];
        Array.Copy(bin, 0, image, 0, colorPtr);
        var outData = new List<byte>(image.Length * 4);
        foreach (var b in image)
        {
            outData.Add(colors[(b >> 6) & 3]);
            outData.Add(colors[(b >> 4) & 3]);
            outData.Add(colors[(b >> 2) & 3]);
            outData.Add(colors[b & 3]);
        }
        return outData.ToArray();
    }
}
