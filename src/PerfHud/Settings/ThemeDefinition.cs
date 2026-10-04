namespace PerfHud.Settings;

public sealed record ThemeDefinition(
    string Name, string Background, string Panel, string Accent, string Success, string Warning,
    string Hot, string Danger, string Cool, string Text, string Muted)
{
    public static readonly ThemeDefinition[] BuiltIn =
    {
        new("Signal",   "#151412", "#1E1C19", "#FF6A2B", "#A6D16B", "#F2C14E", "#FF8B3D", "#FF4D3D", "#8FB8C9", "#F3EFE7", "#8C867B"),
        new("Paper",    "#F3F0E8", "#E8E3D8", "#D9481C", "#3E7D3A", "#B07A00", "#D9661C", "#C0262D", "#3C6E8F", "#1C1A16", "#7A746A"),
        new("Bone",     "#E9E6DE", "#DCD8CE", "#1C1C1C", "#2F6F3A", "#9A6B00", "#C2581C", "#B3261E", "#3E6A80", "#151515", "#6E6A62"),
        new("Amber",    "#0F0A03", "#1A1206", "#FFB040", "#FFB040", "#FFD27A", "#FF8A3D", "#FF4A3D", "#C9A46A", "#FFC266", "#8A6A35"),
        new("Phosphor", "#040A05", "#0A140C", "#6CFF8A", "#6CFF8A", "#E8F56A", "#FFB24A", "#FF5A4A", "#7FD6B0", "#A8FFB8", "#4F8A5B"),
        new("Mono",     "#0E0E0E", "#191919", "#FFFFFF", "#E6E6E6", "#E8C468", "#E8915A", "#EF5B5B", "#BDBDBD", "#F2F2F2", "#8A8A8A"),
        new("Clay",     "#1E1613", "#2A1E1A", "#E07856", "#B5C98A", "#E8B85C", "#E8875C", "#E0524A", "#9DB7B3", "#F4E8E1", "#9B8A82"),
        new("Moss",     "#121610", "#1A2017", "#C9DA5A", "#9CCB6B", "#E0B64E", "#E8894A", "#E06A55", "#8FB9A8", "#E8EDDC", "#858E77"),
        new("Sand",     "#EDE4D3", "#E1D6C1", "#8A5A2B", "#5E7D3A", "#A87400", "#C4652A", "#A8322A", "#4F7486", "#2A2219", "#7E7160"),
        new("Rust",     "#17110E", "#221915", "#C8502A", "#A9B66A", "#D9A441", "#D97A3A", "#E04A35", "#8AA6A8", "#EFE2D6", "#8F7B6E"),
        new("Plum",     "#18121A", "#221A25", "#D88FB0", "#A8C98A", "#E6BE6A", "#E8916A", "#E8606A", "#9FB2D6", "#F0E6EE", "#8E7F8E"),
        new("Slate",    "#14171A", "#1C2024", "#E8D9B0", "#9BC28A", "#E2B45A", "#E58E5A", "#E05F55", "#9AB0C0", "#E6E9EC", "#7F8890"),
        new("Ochre",    "#1A160D", "#241F13", "#D9A520", "#9DBA5E", "#E8C25A", "#E0893A", "#D9503A", "#93A99A", "#F2EAD3", "#8F856A"),
        new("Tide",     "#0F1716", "#16201F", "#E9A76B", "#7FC4A0", "#E3C26A", "#E8885A", "#E2574C", "#7FB0B6", "#E3EEEC", "#7A8E8B"),
        new("Classic",  "#0A0D12", "#11161D", "#00D9FF", "#42E88A", "#FFC857", "#FF8C42", "#FF4D6D", "#4CC9F0", "#F2F5F7", "#7C8795"),
    };

    /// <summary>Okabe–Ito based palette: distinguishable for the common forms of color-vision deficiency.</summary>
    public static readonly (string Cool, string Ok, string Warm, string Hot, string Critical) ColorBlindSafe =
        ("#56B4E9", "#009E73", "#F0E442", "#E69F00", "#D55E00");
}

/// <summary>
/// A "look" is a bundle of shape/typography settings (not colors). Applying one just sets the individual
/// appearance properties, so everything stays fine-tunable afterwards.
/// </summary>
public sealed record HudLook(string Name, string Description, Action<AppearanceSettings> Set);

public static class HudLooks
{
    public static readonly HudLook[] All =
    {
        new("Instrument", "Squared panel, accent edge, segmented bars, label tape headers", a =>
        {
            a.PanelStyle = PanelStyle.Solid; a.PanelEdge = PanelEdge.Left; a.CornerRadius = 2; a.Border = true; a.BorderWidth = 1;
            a.LabelFont = "Segoe UI Semibold"; a.ValueFont = "Bahnschrift"; a.LabelWeight = WeightOption.Regular; a.ValueWeight = WeightOption.SemiBold;
            a.LabelCase = LabelCase.Upper; a.LabelPosition = LabelPosition.Left; a.HeaderStyle = HeaderStyle.Tape;
            a.BarStyle = BarStyle.Segmented; a.GraphStyle = GraphStyle.Area; a.GaugeStyle = GaugeStyle.Half;
            a.ShowIcons = false; a.TextShadow = false; a.Shadow = false; a.PanelPadding = 9; a.RowSpacing = 2.5; a.ColumnSpacing = 8;
        }),
        new("Terminal", "Monospace everything, hard corners, column graphs", a =>
        {
            a.PanelStyle = PanelStyle.Solid; a.PanelEdge = PanelEdge.None; a.CornerRadius = 0; a.Border = true; a.BorderWidth = 1;
            a.LabelFont = "Cascadia Mono"; a.ValueFont = "Cascadia Mono"; a.LabelWeight = WeightOption.Regular; a.ValueWeight = WeightOption.Regular;
            a.LabelCase = LabelCase.Lower; a.LabelPosition = LabelPosition.Left; a.HeaderStyle = HeaderStyle.Plain;
            a.BarStyle = BarStyle.Segmented; a.GraphStyle = GraphStyle.Columns; a.GaugeStyle = GaugeStyle.Ring;
            a.ShowIcons = false; a.TextShadow = false; a.Shadow = false; a.PanelPadding = 8; a.RowSpacing = 1.5; a.ColumnSpacing = 10;
        }),
        new("Float", "No panel at all — bold numbers with a soft halo", a =>
        {
            a.PanelStyle = PanelStyle.Bare; a.PanelEdge = PanelEdge.None; a.CornerRadius = 0; a.Border = false;
            a.LabelFont = "Bahnschrift"; a.ValueFont = "Bahnschrift"; a.LabelWeight = WeightOption.SemiBold; a.ValueWeight = WeightOption.Bold;
            a.LabelCase = LabelCase.Upper; a.LabelPosition = LabelPosition.Left; a.HeaderStyle = HeaderStyle.Plain;
            a.BarStyle = BarStyle.Line; a.GraphStyle = GraphStyle.Line; a.GaugeStyle = GaugeStyle.Arc;
            a.ShowIcons = false; a.TextShadow = true; a.Shadow = false; a.PanelPadding = 4; a.RowSpacing = 1.5; a.ColumnSpacing = 10;
        }),
        new("Stacked", "Small labels sitting above big values", a =>
        {
            a.PanelStyle = PanelStyle.Solid; a.PanelEdge = PanelEdge.Top; a.CornerRadius = 2; a.Border = true; a.BorderWidth = 1;
            a.LabelFont = "Segoe UI"; a.ValueFont = "Bahnschrift"; a.LabelWeight = WeightOption.Regular; a.ValueWeight = WeightOption.SemiBold;
            a.LabelCase = LabelCase.Upper; a.LabelPosition = LabelPosition.Above; a.HeaderStyle = HeaderStyle.Rule;
            a.BarStyle = BarStyle.Square; a.GraphStyle = GraphStyle.Area; a.GaugeStyle = GaugeStyle.Arc;
            a.ShowIcons = false; a.TextShadow = false; a.Shadow = false; a.PanelPadding = 10; a.RowSpacing = 4; a.ColumnSpacing = 12;
        }),
        new("Outline", "Transparent panel with a thin frame and line graphs", a =>
        {
            a.PanelStyle = PanelStyle.Outline; a.PanelEdge = PanelEdge.None; a.CornerRadius = 4; a.Border = true; a.BorderWidth = 1;
            a.LabelFont = "Segoe UI"; a.ValueFont = "Bahnschrift"; a.LabelWeight = WeightOption.Regular; a.ValueWeight = WeightOption.SemiBold;
            a.LabelCase = LabelCase.Upper; a.LabelPosition = LabelPosition.Left; a.HeaderStyle = HeaderStyle.Rule;
            a.BarStyle = BarStyle.Line; a.GraphStyle = GraphStyle.Line; a.GaugeStyle = GaugeStyle.Ring;
            a.ShowIcons = true; a.TextShadow = true; a.Shadow = false; a.PanelPadding = 8; a.RowSpacing = 2.5; a.ColumnSpacing = 8;
        }),
        new("Soft", "Rounded panel, icons, title-case labels", a =>
        {
            a.PanelStyle = PanelStyle.Solid; a.PanelEdge = PanelEdge.None; a.CornerRadius = 12; a.Border = true; a.BorderWidth = 1;
            a.LabelFont = "Segoe UI"; a.ValueFont = "Segoe UI Variable Display Semibold"; a.LabelWeight = WeightOption.Regular; a.ValueWeight = WeightOption.SemiBold;
            a.LabelCase = LabelCase.Title; a.LabelPosition = LabelPosition.Left; a.HeaderStyle = HeaderStyle.Rule;
            a.BarStyle = BarStyle.Rounded; a.GraphStyle = GraphStyle.Area; a.GaugeStyle = GaugeStyle.Arc;
            a.ShowIcons = true; a.TextShadow = false; a.Shadow = true; a.PanelPadding = 10; a.RowSpacing = 3; a.ColumnSpacing = 8;
        }),
    };

    public static void Apply(HudLook look, AppearanceSettings a)
    {
        look.Set(a);
        a.Look = look.Name;
    }
}
