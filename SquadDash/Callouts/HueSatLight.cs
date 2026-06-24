using System.Windows.Media;

namespace SquadDash;
public class HueSatLight {
    double hue;
    double lightness;
    double saturation;

    public HueSatLight() {
        hue = 0;
        saturation = 0;
        lightness = 0;
    }

    public HueSatLight(Color color) {
        var dc = System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        Hue = dc.GetHue() / 360.0; // Convert from range of 0-360 to 0.0-1.0.
        Lightness = dc.GetBrightness();
        Saturation = dc.GetSaturation();
    }

    /// <summary>
    /// Returns an equivalent Color object converted to gray scale.
    /// </summary>
    public Color AsGrayScale {
        get {
            var lColor = AsRGB;
            var lGrayScaleLevel = (byte)(lColor.R * 0.3 + lColor.G * 0.59 + lColor.B * 0.11);
            return Color.FromArgb(AsRGB.A, lGrayScaleLevel, lGrayScaleLevel, lGrayScaleLevel);
        }
    }

    /// <summary> 
    /// Returns an equivalent Color instance.
    /// </summary> 
    public Color AsRGB {
        get {
            var red = 0d;
            var green = 0d;
            var blue = 0d;
            if(Lightness == 0)
                // Completely black.
                red = green = blue = 0;
            else if(Saturation == 0)
                // No color; somewhere on the gray scale.
                red = green = blue = Lightness;
            else {
                var temp2 = Lightness <= 0.5
                    ? Lightness * (1.0 + Saturation)
                    : Lightness + Saturation - Lightness * Saturation;
                var temp1 = 2.0 * Lightness - temp2;

                var hueShift = new double[] { (Hue + 1.0 / 3.0), Hue, (Hue - 1.0 / 3.0) };
                var colorArray = new double[] { 0, 0, 0 };

                for(var i = 0; i < 3; i++) {
                    if(hueShift[i] < 0)
                        hueShift[i] += 1.0;

                    if(hueShift[i] > 1)
                        hueShift[i] -= 1.0;

                    if(6.0 * hueShift[i] < 1.0)
                        colorArray[i] = temp1 + (temp2 - temp1) * hueShift[i] * 6.0;
                    else if(2.0 * hueShift[i] < 1.0)
                        colorArray[i] = temp2;
                    else if(3.0 * hueShift[i] < 2.0)
                        colorArray[i] = temp1 + (temp2 - temp1) * (2.0 / 3.0 - hueShift[i]) * 6.0;
                    else
                        colorArray[i] = temp1;
                }
                red = colorArray[0];
                green = colorArray[1];
                blue = colorArray[2];
            }
            return Color.FromArgb(255, (byte)(255 * red), (byte)(255 * green), (byte)(255 * blue));
        }
    }

    /// <summary>
    /// Returns the percentage on the gray scale.
    /// </summary>
    public float GrayScale => AsGrayScale.R / 255.0f;

    public double Hue { get => hue; set => hue = value > 1 ? 1 : value < 0 ? 0 : value; }

    /// <summary>
    /// Returns true if this color is darker than medium gray.
    /// </summary>
    public bool IsDark => AsGrayScale.R < 128;

    /// <summary>
    /// Returns true if saturation is at zero.
    /// </summary>
    public bool IsGrayScale => saturation == 0.0;

    /// <summary>
    /// Returns true if this color is brighter than medium gray.
    /// </summary>
    public bool IsLight => AsGrayScale.R > 128;

    public double Lightness { get => lightness; set => lightness = value > 1 ? 1 : value < 0 ? 0 : value; }

    public double Saturation { get => saturation; set => saturation = value > 1 ? 1 : value < 0 ? 0 : value; }
}
