namespace SpriteEditXL.Model;

/// <summary>
/// Sprite composition modes. Bit layout matches the original SprEd JavaScript:
/// bit0 = missile, bit1 = missile-on-left, bit2 = player 2+3 enabled.
/// </summary>
public enum SpriteMode
{
    P0P1 = 0,
    PM0PM1 = 1,
    MP0MP1 = 2,
    P0P1P2P3 = 4,
    PM0PM1PM2PM3 = 5,
    MP0MP1MP2MP3 = 6
}

public static class SpriteModeExtensions
{
    public static bool IsMissileMode(this SpriteMode m) => ((int)m & 1) == 1;
    public static bool IsMissileOnLeft(this SpriteMode m) => ((int)m & 2) == 2;
    public static bool IsPlayer23Mode(this SpriteMode m) => ((int)m & 4) == 4;
    public static int PlayerCount(this SpriteMode m) => m.IsPlayer23Mode() ? 4 : 2;
}
