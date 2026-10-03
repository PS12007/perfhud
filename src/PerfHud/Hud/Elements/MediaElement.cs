using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PerfHud.Monitoring;
using PerfHud.Monitoring.Monitors;
using PerfHud.Rendering;

namespace PerfHud.Hud.Elements;

/// <summary>Now playing: cover art, title, artist · app, and a progress bar with position / length.</summary>
public sealed class MediaElement : HudElement
{
    private readonly Image _art = new() { Stretch = Stretch.UniformToFill };
    private readonly FrameworkElement? _artBox;
    private readonly FrameworkElement _placeholder;
    private readonly TextBlock _title, _sub, _time;
    private readonly BarFill _bar;

    public MediaElement(HudComponent c, HudStyle s) : base(c, s)
    {
        double artSize = Math.Max(20, (C.Height > 0 ? C.Height : 46) * Fs);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _placeholder = Icons.Create("music", S.Muted, artSize * 0.42);
        _placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        _placeholder.VerticalAlignment = VerticalAlignment.Center;
        if (C.ShowIcon)
        {
            var cell = new Grid();
            cell.Children.Add(_placeholder);
            cell.Children.Add(_art);
            var box = new Border
            {
                Width = artSize, Height = artSize, Background = S.Track, Child = cell, ClipToBounds = true,
                CornerRadius = new CornerRadius(Math.Min(3, S.CornerRadius)), Margin = new Thickness(0, 0, 9 * Fs, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _artBox = box;
            grid.Children.Add(box);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = C.Width > 0 ? Math.Max(60, C.Width - artSize) : 230 * Fs };
        if (ShowLabel)
        {
            var lbl = MakeLabel(string.IsNullOrEmpty(C.Label) ? "Now playing" : C.Label);
            lbl.FontSize = S.SmallSize * Fs;
            text.Children.Add(lbl);
        }
        _title = MakeValueBlock(S.ValueSize * Fs * 0.92);
        if (OverrideBrush != null) _title.Foreground = OverrideBrush;
        _sub = MakeLabel("");
        _sub.FontWeight = FontWeights.Normal;
        text.Children.Add(_title);
        text.Children.Add(_sub);

        var foot = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
        _time = MakeLabel("");
        _time.FontWeight = FontWeights.Normal;
        _time.FontSize = S.SmallSize * Fs;
        _time.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(_time, Dock.Right);
        foot.Children.Add(_time);
        _bar = MakeBar(Math.Max(2, S.BarThickness * 0.6 * Fs), 60);
        _bar.VerticalAlignment = VerticalAlignment.Center;
        _bar.Fill = OverrideBrush ?? S.Accent;
        foot.Children.Add(_bar);
        text.Children.Add(foot);

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Child = grid;
    }

    public override bool IsShown(HudRenderContext ctx) =>
        C.ShowWhen == ShowCondition.WhenAvailable ? ctx.Store.GetObject<MediaSnapshot>("media.snapshot") != null : base.IsShown(ctx);

    public override void Refresh(HudRenderContext ctx)
    {
        var m = ctx.Store.GetObject<MediaSnapshot>("media.snapshot");
        if (m == null)
        {
            SetText(_title, "Nothing playing");
            SetFg(_title, S.Muted);
            SetText(_sub, ctx.Store.GetReason("media.progress") ?? "");
            SetText(_time, "");
            _bar.AnimateTo(0, 0);
            SetArt(null);
            return;
        }

        SetText(_title, string.IsNullOrEmpty(m.Title) ? "Unknown title" : m.Title);
        SetFg(_title, m.Playing ? OverrideBrush ?? S.Text : S.Muted);
        var sub = string.Join(" · ", new[] { m.Artist, m.App }.Where(x => !string.IsNullOrEmpty(x)));
        SetText(_sub, m.Playing ? sub : (sub.Length > 0 ? "Paused · " + sub : "Paused"));

        double pos = m.PositionAt(Environment.TickCount64);
        if (m.Duration > 0)
        {
            SetText(_time, $"{MetricRegistry.FormatTrackTime(pos)} / {MetricRegistry.FormatTrackTime(m.Duration)}");
            _bar.Visibility = Visibility.Visible;
            _bar.AnimateTo(pos / m.Duration, S.AnimMs);
        }
        else
        {
            SetText(_time, m.Playing ? "live" : "");
            _bar.Visibility = Visibility.Collapsed;
        }
        SetArt(m.Art);
    }

    private void SetArt(ImageSource? art)
    {
        if (_artBox == null || ReferenceEquals(_art.Source, art)) return;
        _art.Source = art;
        _placeholder.Visibility = art == null ? Visibility.Visible : Visibility.Collapsed;
    }
}
