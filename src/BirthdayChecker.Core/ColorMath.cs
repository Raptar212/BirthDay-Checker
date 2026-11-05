using System.Globalization;

namespace BirthdayChecker.Core;

/// <summary>A color as the picker holds it: hue 0..360, saturation and value 0..1. Kept as HSV so black doesn't forget its hue.</summary>
public readonly record struct Hsv(double H, double S, double V);

/// <summary>Conversions between HSV, RGB bytes and "#RRGGBB".</summary>
public static class ColorMath
{
    public static (byte R, byte G, byte B) ToRgb(Hsv c)
    {
        double h = (c.H % 360 + 360) % 360 / 60;
        double chroma = c.V * c.S;
        double x = chroma * (1 - Math.Abs(h % 2 - 1));
        var (r, g, b) = (int)h switch
        {
            0 => (chroma, x, 0d),
            1 => (x, chroma, 0d),
            2 => (0d, chroma, x),
            3 => (0d, x, chroma),
            4 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };
        double m = c.V - chroma;
        return (Byte(r + m), Byte(g + m), Byte(b + m));
    }

    /// <summary>Hue 0 for grays.</summary>
    public static Hsv ToHsv(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double hue = delta == 0 ? 0
            : max == r ? 60 * ((g - b) / delta)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        return new Hsv(hue < 0 ? hue + 360 : hue, max == 0 ? 0 : delta / max, max / 255d);
    }

    public static string ToHex(Hsv c)
    {
        var (r, g, b) = ToRgb(c);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>"#RRGGBB" or "RRGGBB"; anything else is false.</summary>
    public static bool TryParseHex(string? text, out Hsv color)
    {
        color = default;
        var s = text?.Trim().TrimStart('#');
        if (s is not { Length: 6 } || !int.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int rgb))
            return false;
        color = ToHsv((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
}
