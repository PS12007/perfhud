using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PerfHud.Settings;

namespace PerfHud.Hud;

/// <summary>Panel styling shared by the overlay and the live preview in settings: background, frame, accent edge, halo.</summary>
public static class HudChrome
{
    private const string EdgeTag = "hud-edge";

    public static void Apply(Border panel, DockPanel dock, FrameworkElement content, HudStyle st, bool shadow)
    {
        double radius = st.CornerRadius;
        double pad = st.PanelPadding;
        panel.CornerRadius = new CornerRadius(radius);
        panel.Background = st.Background;
        panel.BorderBrush = st.PanelBorder;
        panel.BorderThickness = new Thickness(st.PanelBorder.Color.A == 0 ? 0 : st.BorderWidth);
        panel.Padding = new Thickness(pad + 1, pad * 0.85, pad + 1, pad * 0.85);
        bool solid = st.PanelStyle == PanelStyle.Solid;
        panel.Effect = shadow && solid && !st.HighContrast
            ? new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = st.LightBackground ? 0.25 : 0.55, Color = Colors.Black, RenderingBias = RenderingBias.Performance }
            : null;
        panel.Margin = shadow && solid ? new Thickness(14) : new Thickness(0);

        // Halo behind text: keeps numbers readable over busy game scenes when there's little or no panel.
        var bg = st.Background.Color;
        content.Effect = st.TextShadow
            ? new DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 1, Color = Color.FromRgb(bg.R, bg.G, bg.B), RenderingBias = RenderingBias.Performance }
            : null;

        // Accent edge stripe, flush with the panel border.
        foreach (var old in dock.Children.OfType<Border>().Where(b => EdgeTag.Equals(b.Tag)).ToList()) dock.Children.Remove(old);
        if (st.PanelEdge != PanelEdge.None)
        {
            double t = Math.Max(2, Math.Round(pad * 0.35));
            var edge = new Border { Tag = EdgeTag, Background = st.Accent, IsHitTestVisible = false };
            if (st.PanelEdge == PanelEdge.Left)
            {
                edge.Width = t;
                edge.Margin = new Thickness(-pad - 1, -pad * 0.85, pad, -pad * 0.85);
                edge.CornerRadius = new CornerRadius(radius, 0, 0, radius);
                DockPanel.SetDock(edge, Dock.Left);
            }
            else
            {
                edge.Height = t;
                edge.Margin = new Thickness(-pad - 1, -pad * 0.85, -pad - 1, pad * 0.7);
                edge.CornerRadius = new CornerRadius(radius, radius, 0, 0);
                DockPanel.SetDock(edge, Dock.Top);
            }
            dock.Children.Insert(0, edge);
        }
    }
}
