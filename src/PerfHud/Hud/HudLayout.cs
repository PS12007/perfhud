using System.Text.Json.Serialization;
using PerfHud.Core;

namespace PerfHud.Hud;

public enum ComponentType
{
    Number, BigNumber, Percentage, ProgressBar, Graph, FrameTimeGraph, Gauge,
    Text, Icon, Divider, Spacer, CoreGrid, SensorList, DriveList,
    Trend, Stats, Template,
}

/// <summary>One cell of a HUD layout. Layouts are grids: components occupy (Col, Row) with spans.</summary>
public sealed class HudComponent : Observable
{
    private string _id = Guid.NewGuid().ToString("N")[..8];
    private ComponentType _type;
    private string _metric = "", _secondary = "", _label = "", _text = "", _icon = "", _color = "";
    private int _col, _row, _colSpan = 1, _rowSpan = 1, _graphSeconds;
    private bool _ratio, _detailOnly, _showLabel = true, _showIcon = true;
    private double _fontScale = 1, _height, _width;

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

    [JsonIgnore] public bool IsBuiltIn { get; init; }

    [JsonIgnore] public int Columns => Components.Count == 0 ? 1 : Components.Max(c => c.ColEnd);
    [JsonIgnore] public int Rows => Components.Count == 0 ? 1 : Components.Max(c => c.RowEnd);

    public HudLayout Clone(string name)
    {
        using var _ = Quiet();
        return new HudLayout { Name = name, Components = new ObservableList<HudComponent>(Components.Select(c => c.Clone())) };
    }

    public override string ToString() => Name;
}
