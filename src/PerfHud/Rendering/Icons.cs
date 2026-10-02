using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfPath = System.Windows.Shapes.Path;

namespace PerfHud.Rendering;

/// <summary>Minimal stroke icon set (24×24 grid, round caps). Vector, so it stays crisp at any HUD scale.</summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Data = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cpu"] = "M9,2.5 V5 M15,2.5 V5 M9,19 V21.5 M15,19 V21.5 M2.5,9 H5 M2.5,15 H5 M19,9 H21.5 M19,15 H21.5 M7,5 H17 A2,2 0 0 1 19,7 V17 A2,2 0 0 1 17,19 H7 A2,2 0 0 1 5,17 V7 A2,2 0 0 1 7,5 Z M10,10 H14 V14 H10 Z",
        ["gpu"] = "M2,7 H20 A1.5,1.5 0 0 1 21.5,8.5 V15.5 A1.5,1.5 0 0 1 20,17 H2 M2,5 V20 M6,17 V19.5 M10,17 V19.5 M14,12 m-2.6,0 a2.6,2.6 0 1 0 5.2,0 a2.6,2.6 0 1 0 -5.2,0",
        ["ram"] = "M3,8 H21 V15 H3 Z M7,8 V11.5 M11,8 V11.5 M15,8 V11.5 M6,15 V18.5 M10,15 V18.5 M14,15 V18.5 M18,15 V18.5",
        ["vram"] = "M5,6 H19 V18 H5 Z M9,10 H15 V14 H9 Z M9,3 V6 M15,3 V6 M9,18 V21 M15,18 V21 M2,9 H5 M2,15 H5 M19,9 H22 M19,15 H22",
        ["disk"] = "M4,5 H20 A1,1 0 0 1 21,6 V18 A1,1 0 0 1 20,19 H4 A1,1 0 0 1 3,18 V6 A1,1 0 0 1 4,5 Z M3,14 H21 M7,16.5 H7.01 M11,16.5 H11.01",
        ["net"] = "M5,12.6 A10,10 0 0 1 19,12.6 M8.5,16.1 A5,5 0 0 1 15.5,16.1 M2,9 A15,15 0 0 1 22,9 M12,20 H12.01",
        ["down"] = "M12,4 V20 M6,14 L12,20 L18,14",
        ["up"] = "M12,20 V4 M6,10 L12,4 L18,10",
        ["battery"] = "M4,7 H16 A2,2 0 0 1 18,9 V15 A2,2 0 0 1 16,17 H4 A2,2 0 0 1 2,15 V9 A2,2 0 0 1 4,7 Z M21.5,11 V13 M6,10.5 V13.5",
        ["plug"] = "M9,2 V7 M15,2 V7 M6,7 H18 V10.5 A6,6 0 0 1 6,10.5 Z M12,16.5 V22",
        ["power"] = "M13,2 L4,14 H12 L11,22 L20,10 H12 Z",
        ["temp"] = "M14,14.76 V3.5 A2.5,2.5 0 0 0 9,3.5 V14.76 A4.5,4.5 0 1 0 14,14.76 Z",
        ["fps"] = "M12,14 L16.5,9.5 M3.34,19 A10,10 0 1 1 20.66,19",
        ["frametime"] = "M2.5,12 H6.5 L9.5,5 L14,19 L17,12 H21.5",
        ["clock"] = "M12,12 m-9,0 a9,9 0 1 0 18,0 a9,9 0 1 0 -18,0 M12,7 V12 L15,14",
        ["fan"] = "M12,12 m-1.8,0 a1.8,1.8 0 1 0 3.6,0 a1.8,1.8 0 1 0 -3.6,0 M12,10.2 C11,6 8,3.5 6,5.5 C4.5,7 7,10 10.2,11.4 M13.8,12 C18,11 20.5,8 18.5,6 C17,4.5 14,7 12.6,10.2 M12,13.8 C13,18 16,20.5 18,18.5 C19.5,17 17,14 13.8,12.6 M10.2,12 C6,13 3.5,16 5.5,18 C7,19.5 10,17 11.4,13.8",
        ["laptop"] = "M4,5 H20 V15 H4 Z M2,19 H22",
        ["display"] = "M3,4 H21 V16 H3 Z M8,20 H16 M12,16 V20",
        ["warn"] = "M12,3 L22,20 H2 Z M12,9.5 V13.5 M12,17 H12.01",
        ["game"] = "M6,8 H18 A4,4 0 0 1 22,12 V14 A3,3 0 0 1 16.8,16 L15.5,14.5 H8.5 L7.2,16 A3,3 0 0 1 2,14 V12 A4,4 0 0 1 6,8 Z M7,11 V13 M6,12 H8 M15,12 H15.01 M18,11 H18.01",
        ["settings"] = "M12,12 m-3,0 a3,3 0 1 0 6,0 a3,3 0 1 0 -6,0 M12,2 V5 M12,19 V22 M2,12 H5 M19,12 H22 M4.9,4.9 L7,7 M17,17 L19.1,19.1 M4.9,19.1 L7,17 M17,7 L19.1,4.9",
        ["layers"] = "M12,2 L22,7 L12,12 L2,7 Z M2,17 L12,22 L22,17 M2,12 L12,17 L22,12",
        ["history"] = "M3,12 A9,9 0 1 0 5.6,5.6 M3,3.5 V8.5 H8 M12,7 V12 L15,14",
        ["bell"] = "M6,8 A6,6 0 0 1 18,8 C18,15 21,17 21,17 H3 C3,17 6,15 6,8 Z M10.3,21 A1.94,1.94 0 0 0 13.7,21",
        ["profile"] = "M12,8 m-4,0 a4,4 0 1 0 8,0 a4,4 0 1 0 -8,0 M4,21 A8,8 0 0 1 20,21",
        ["sliders"] = "M4,21 V14 M4,10 V3 M12,21 V12 M12,8 V3 M20,21 V16 M20,12 V3 M1,14 H7 M9,8 H15 M17,16 H23",
        ["palette"] = "M12,2 A10,10 0 1 0 12,22 C13.5,22 14,21 14,20 C14,18.5 13,18 13,17 C13,16 14,15 15,15 H17 A5,5 0 0 0 22,10 C22,5.5 17.5,2 12,2 Z M7.5,10.5 H7.51 M10.5,6.5 H10.51 M15.5,7.5 H15.51",
        ["keyboard"] = "M2,6 H22 V18 H2 Z M6,10 H6.01 M10,10 H10.01 M14,10 H14.01 M18,10 H18.01 M7,14 H17",
        ["activity"] = "M22,12 H18 L15,21 L9,3 L6,12 H2",
        ["grid"] = "M3,3 H10 V10 H3 Z M14,3 H21 V10 H14 Z M14,14 H21 V21 H14 Z M3,14 H10 V21 H3 Z",
        ["shield"] = "M12,22 C12,22 20,18 20,12 V5 L12,2 L4,5 V12 C4,18 12,22 12,22 Z",
        ["text"] = "M4,7 V4 H20 V7 M9,20 H15 M12,4 V20",
        ["info"] = "M12,12 m-10,0 a10,10 0 1 0 20,0 a10,10 0 1 0 -20,0 M12,16 V12 M12,8 H12.01",
        ["plus"] = "M12,5 V19 M5,12 H19",
        ["copy"] = "M9,9 H20 V20 H9 Z M5,15 H4 V4 H15 V5",
        ["trash"] = "M3,6 H21 M8,6 V4 H16 V6 M19,6 L18,20 H6 L5,6 M10,11 V17 M14,11 V17",
        ["arrowup"] = "M12,19 V5 M5,12 L12,5 L19,12",
        ["arrowdown"] = "M12,5 V19 M19,12 L12,19 L5,12",
        ["lock"] = "M5,11 H19 V21 H5 Z M8,11 V7 A4,4 0 0 1 16,7 V11",
        ["unlock"] = "M5,11 H19 V21 H5 Z M8,11 V7 A4,4 0 0 1 15.9,6",
        ["pause"] = "M7,4 H10 V20 H7 Z M14,4 H17 V20 H14 Z",
        ["record"] = "M12,12 m-6,0 a6,6 0 1 0 12,0 a6,6 0 1 0 -12,0",
        ["eye"] = "M2,12 C2,12 5,5 12,5 C19,5 22,12 22,12 C22,12 19,19 12,19 C5,19 2,12 2,12 Z M12,12 m-3,0 a3,3 0 1 0 6,0 a3,3 0 1 0 -6,0",
        ["divider"] = "M3,12 H21",
        ["spacer"] = "M3,8 V16 M21,8 V16 M3,12 H21",
        ["gauge"] = "M12,14 L15,10 M3.34,19 A10,10 0 1 1 20.66,19",
        ["bar"] = "M3,10 H21 V14 H3 Z M3,10 H13 V14",
        ["graph"] = "M3,3 V21 H21 M7,15 L11,10 L14,13 L20,6",
        ["number"] = "M4,9 H20 M4,15 H20 M10,3 L8,21 M16,3 L14,21",
        ["percent"] = "M19,5 L5,19 M6.5,6.5 m-2.5,0 a2.5,2.5 0 1 0 5,0 a2.5,2.5 0 1 0 -5,0 M17.5,17.5 m-2.5,0 a2.5,2.5 0 1 0 5,0 a2.5,2.5 0 1 0 -5,0",
        ["list"] = "M8,6 H21 M8,12 H21 M8,18 H21 M3,6 H3.01 M3,12 H3.01 M3,18 H3.01",
        ["cores"] = "M4,20 V12 M8,20 V7 M12,20 V14 M16,20 V5 M20,20 V10",
    };

    private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> Names => Data.Keys;

    public static Geometry Get(string? name)
    {
        name = string.IsNullOrEmpty(name) || !Data.ContainsKey(name) ? "activity" : name;
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var g)) return g;
            g = Geometry.Parse(Data[name]);
            g.Freeze();
            Cache[name] = g;
            return g;
        }
    }

    /// <summary>Creates a crisp stroke icon element of the given size.</summary>
    public static FrameworkElement Create(string? name, Brush brush, double size, double stroke = 2.0)
    {
        var path = new WpfPath
        {
            Data = Get(name),
            Stroke = brush,
            StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            SnapsToDevicePixels = false,
        };
        var canvas = new Canvas { Width = 24, Height = 24, Children = { path } };
        return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
    }
}
