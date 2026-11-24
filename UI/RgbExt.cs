using Avalonia.Media;
using SpriteEditXL.Model;

namespace SpriteEditXL.UI;

public static class RgbExt
{
    public static Color ToAvalonia(this Rgb c) => Color.FromRgb(c.R, c.G, c.B);
    public static IBrush ToBrush(this Rgb c) => new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
}
