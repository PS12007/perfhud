using System.Text.Json.Serialization;
using PerfHud.Core;

namespace PerfHud.Hud;

public enum ComponentType
{
    Number, BigNumber, Percentage, ProgressBar, Graph, FrameTimeGraph, Gauge,
    Text, Icon, Divider, Spacer, CoreGrid, SensorList, DriveList,
    Trend, Stats, Template, Media,
}

/// <summary>Horizontal placement of a component inside its grid cell.</summary>
public enum CellAlign { Auto, Left, Center, Right, Stretch }
public enum CellVAlign { Auto, Top, Center, Bottom }

/// <summary>When a component is shown. Hidden components take no space.</summary>
public enum ShowCondition { Always, WhenAvailable, WhenWarning, OnBattery, OnAC, WhenMediaPlaying, WhenGameRunning }

/// <summary>One cell of a HUD layout. Layouts are grids: components occupy (Col, Row) with spans.</summary>
public sealed class HudComponent : Observable
{
    private string _id = Guid.NewGuid().ToString("N")[..8];
    private ComponentType _type;
    private string _metric = "", _secondary = "", _label = "", _text = "", _icon = "", _color = "";
    private string _panel = "", _labelColor = "", _background = "", _valueFont = "";
    private int _col, _row, _colSpan = 1, _rowSpan = 1, _graphSeconds, _decimals = -1;
    private bool _ratio, _detailOnly, _showLabel = true, _showIcon = true, _hideUnit;
    private double _fontScale = 1, _height, _width, _nudgeX, _nudgeY, _opacity = 1;
    private CellAlign _align;
    private CellVAlign _valign;
    private ShowCondition _showWhen;

    public string Id { get => _id; set => Set(ref _id, value); }
    public ComponentType Type { get => _type; set => Set(ref _type, value); }
    public string MetricId { get => _metric; set => Set(ref _metric, value ?? ""); }
    /// <summary>Optional second value shown on the same line (e.g. CPU usage + temperature) or as the denominator if <see cref="Ratio"/>.</summary>
    public string SecondaryMetricId { get => _secondary; set => Set(ref _secondary, value ?? ""); }
    /// <summary>Overrides the metric's short label. Empty = automatic.</summary>
    public string Label { get => _label; set => Set(ref _label, value ?? ""); }
    /// <summary>Content of Text components.</summary>
    public string Text { get => _text; set => Set(ref _text, value ?? ""); }
    public string Icon { get => _icon; set => Set(ref _icon, value ?? ""); }
    /// <summary>"" = automatic (severity colors), otherwise #RRGGBB.</summary>
    public string Color { get => _color; set => Set(ref _color, value ?? ""); }
    public int Col { get => _col; set => Set(ref _col, Math.Max(0, value)); }
    public int Row { get => _row; set => Set(ref _row, Math.Max(0, value)); }
    public int ColSpan { get => _colSpan; set => Set(ref _colSpan, Math.Clamp(value, 1, 12)); }
    public int RowSpan { get => _rowSpan; set => Set(ref _rowSpan, Math.Clamp(value, 1, 40)); }
    /// <summary>Show primary/secondary as "a/b unit" (e.g. RAM 11.2/32 GB).</summary>
    public bool Ratio { get => _ratio; set => Set(ref _ratio, value); }
    /// <summary>Hidden in compact mode.</summary>
    public bool DetailOnly { get => _detailOnly; set => Set(ref _detailOnly, value); }
    public bool ShowLabel { get => _showLabel; set => Set(ref _showLabel, value); }
    public bool ShowIcon { get => _showIcon; set => Set(ref _showIcon, value); }
    public double FontScale { get => _fontScale; set => Set(ref _fontScale, Math.Clamp(value, 0.5, 4)); }
    /// <summary>Explicit height (graphs, spacers). 0 = default.</summary>
    public double Height { get => _height; set => Set(ref _height, Math.Max(0, value)); }
    /// <summary>Explicit min width. 0 = automatic.</summary>
    public double Width { get => _width; set => Set(ref _width, Math.Max(0, value)); }
    /// <summary>Graph history window override in seconds (0 = global default).</summary>
    public int GraphSeconds { get => _graphSeconds; set => Set(ref _graphSeconds, value); }

    /// <summary>Floating panel this component lives in ("" = the main HUD). See <see cref="HudPanel"/>.</summary>
    public string Panel { get => _panel; set => Set(ref _panel, value ?? ""); }
    public CellAlign Align { get => _align; set => Set(ref _align, value); }
    public CellVAlign VAlign { get => _valign; set => Set(ref _valign, value); }
    /// <summary>Pixel offset applied after layout, for exact placement without moving neighbours.</summary>
    public double NudgeX { get => _nudgeX; set => Set(ref _nudgeX, Math.Clamp(value, -400, 400)); }
    public double NudgeY { get => _nudgeY; set => Set(ref _nudgeY, Math.Clamp(value, -400, 400)); }
    public double Opacity { get => _opacity; set => Set(ref _opacity, Math.Clamp(value, 0.1, 1)); }
    public ShowCondition ShowWhen { get => _showWhen; set => Set(ref _showWhen, value); }
    /// <summary>Decimal places for numeric values; -1 = automatic.</summary>
    public int Decimals { get => _decimals; set => Set(ref _decimals, Math.Clamp(value, -1, 3)); }
    public bool HideUnit { get => _hideUnit; set => Set(ref _hideUnit, value); }
    /// <summary>"" = theme muted color, otherwise #RRGGBB.</summary>
    public string LabelColor { get => _labelColor; set => Set(ref _labelColor, value ?? ""); }
    /// <summary>Cell background, "" = none. Accepts #AARRGGBB for translucency.</summary>
    public string Background { get => _background; set => Set(ref _background, value ?? ""); }
    /// <summary>Font family for values, "" = the HUD look's value font.</summary>
    public string ValueFont { get => _valueFont; set => Set(ref _valueFont, value ?? ""); }

    public HudComponent Clone()
    {
        using var _ = Quiet();
        return CloneCore();
    }

    private HudComponent CloneCore() => new()
    {
        Type = Type, MetricId = MetricId, SecondaryMetricId = SecondaryMetricId, Label = Label, Text = Text, Icon = Icon,
        Color = Color, Col = Col, Row = Row, ColSpan = ColSpan, RowSpan = RowSpan, Ratio = Ratio, DetailOnly = DetailOnly,
        ShowLabel = ShowLabel, ShowIcon = ShowIcon, FontScale = FontScale, Height = Height, Width = Width, GraphSeconds = GraphSeconds,
        Panel = Panel, Align = Align, VAlign = VAlign, NudgeX = NudgeX, NudgeY = NudgeY, Opacity = Opacity, ShowWhen = ShowWhen,
        Decimals = Decimals, HideUnit = HideUnit, LabelColor = LabelColor, Background = Background, ValueFont = ValueFont,
    };

    [JsonIgnore] public int ColEnd => Col + ColSpan;
    [JsonIgnore] public int RowEnd => Row + RowSpan;

    public bool IsGraph => Type is ComponentType.Graph or ComponentType.FrameTimeGraph;
}

public sealed class HudLayout : Observable
{
    private string _name = "";
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public ObservableList<HudComponent> Components { get; set; } = new();
    /// <summary>Extra overlay windows, each placed independently on screen.</summary>
    public ObservableList<HudPanel> Panels { get; set; } = new();

    [JsonIgnore] public bool IsBuiltIn { get; init; }

    public HudPanel? FindPanel(string name) =>
        string.IsNullOrEmpty(name) ? null : Panels.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    [JsonIgnore] public int Columns => Components.Count == 0 ? 1 : Components.Max(c => c.ColEnd);
    [JsonIgnore] public int Rows => Components.Count == 0 ? 1 : Components.Max(c => c.RowEnd);

    public HudLayout Clone(string name)
    {
        using var _ = Quiet();
        return new HudLayout
        {
            Name = name,
            Components = new ObservableList<HudComponent>(Components.Select(c => c.Clone())),
            Panels = new ObservableList<HudPanel>(Panels.Select(p => p.Clone())),
        };
    }

    public override string ToString() => Name;
}

/// <summary>
/// A floating part of a layout: its own small overlay window with its own screen position, scale and orientation.
/// Components join a panel through <see cref="HudComponent.Panel"/>; put each metric in its own panel to place it anywhere.
/// </summary>
public sealed class HudPanel : Observable
{
    private string _name = "", _monitor = "";
    private bool _enabled = true, _horizontal, _background = true;
    private Settings.HudCorner _corner = Settings.HudCorner.TopRight;
    private double _offX = 16, _offY = 16, _scale = 1, _opacity = 1;
    private int _customX = 200, _customY = 200;

    public string Name { get => _name; set => Set(ref _name, value?.Trim() ?? ""); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public Settings.HudCorner Corner { get => _corner; set => Set(ref _corner, value); }
    public double OffsetX { get => _offX; set => Set(ref _offX, value); }
    public double OffsetY { get => _offY; set => Set(ref _offY, value); }
    /// <summary>Physical pixels from the monitor's top-left when <see cref="Corner"/> is Custom (set by dragging).</summary>
    public int CustomX { get => _customX; set => Set(ref _customX, value); }
    public int CustomY { get => _customY; set => Set(ref _customY, value); }
    /// <summary>"" = same monitor as the main HUD.</summary>
    public string Monitor { get => _monitor; set => Set(ref _monitor, value ?? ""); }
    /// <summary>Multiplies the HUD scale.</summary>
    public double Scale { get => _scale; set => Set(ref _scale, Math.Clamp(value, 0.3, 4)); }
    public double Opacity { get => _opacity; set => Set(ref _opacity, Math.Clamp(value, 0.1, 1)); }
    public bool Horizontal { get => _horizontal; set => Set(ref _horizontal, value); }
    /// <summary>Off = just the values, no panel behind them.</summary>
    public bool ShowBackground { get => _background; set => Set(ref _background, value); }

    public HudPanel Clone()
    {
        using var _ = Quiet();
        return new HudPanel
        {
            Name = Name, Enabled = Enabled, Corner = Corner, OffsetX = OffsetX, OffsetY = OffsetY, CustomX = CustomX, CustomY = CustomY,
            Monitor = Monitor, Scale = Scale, Opacity = Opacity, Horizontal = Horizontal, ShowBackground = ShowBackground,
        };
    }

    public override string ToString() => Name;
}
