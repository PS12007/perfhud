namespace PerfHud.Settings;

public sealed record ThemeDefinition(
    string Name, string Background, string Panel, string Accent, string Success, string Warning,
    string Hot, string Danger, string Cool, string Text, string Muted)
{
    public static readonly ThemeDefinition[] BuiltIn =
    {
        new("Midnight", "#0A0D12", "#11161D", "#00D9FF", "#42E88A", "#FFC857", "#FF8C42", "#FF4D6D", "#4CC9F0", "#F2F5F7", "#7C8795"),
        new("OLED",     "#000000", "#0B0B0D", "#7CF5FF", "#3DDC84", "#FFD166", "#FF9F45", "#FF3B5C", "#5AC8FA", "#FFFFFF", "#8A8F98"),
        new("Ember",    "#0F0B0A", "#18110F", "#FF7A45", "#7BD88F", "#FFC857", "#FF8C42", "#FF4D6D", "#6CC4FF", "#F7F1EE", "#9A8B84"),
        new("Viridian", "#07110E", "#0D1A16", "#3DFFB0", "#3DFFB0", "#F4D35E", "#FF9F45", "#FF5470", "#53D8FB", "#EAF7F2", "#7E958C"),
        new("Synth",    "#0D0814", "#160F21", "#FF4FD8", "#42E88A", "#FFD23F", "#FF8C42", "#FF3864", "#47E5FF", "#F5EEFF", "#8F82A8"),
        new("Frost",    "#0E1420", "#152033", "#9EC9FF", "#7BE0AD", "#FFD479", "#FFA26B", "#FF6B81", "#9EC9FF", "#EEF4FF", "#8796AE"),
        new("Graphite", "#121212", "#1B1B1B", "#E0E0E0", "#A5D6A7", "#FFE082", "#FFB74D", "#EF9A9A", "#90CAF9", "#F5F5F5", "#8E8E8E"),
    };

    /// <summary>Okabe–Ito based palette: distinguishable for the common forms of color-vision deficiency.</summary>
    public static readonly (string Cool, string Ok, string Warm, string Hot, string Critical) ColorBlindSafe =
        ("#56B4E9", "#009E73", "#F0E442", "#E69F00", "#D55E00");
}
