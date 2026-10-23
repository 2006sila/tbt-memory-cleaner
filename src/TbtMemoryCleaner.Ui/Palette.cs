using System.Windows.Media;

namespace TbtMemoryCleaner.Ui;

/// <summary>与 ToolKit 现有配色一致（取自 TBTS 反编译出的调色板）。</summary>
internal sealed class Palette
{
    public Color Canvas;
    public Color Surface;
    public Color SurfaceRaised;
    public Color Border;
    public Color Text;
    public Color Muted;
    public Color Accent;
    public Color AccentSoft;
    public Color Success;
    public Color Warning;
    public Color Danger;

    public static readonly Palette Dark = new()
    {
        Canvas = C("#0A1020"),
        Surface = C("#151F33"),
        SurfaceRaised = C("#1B2840"),
        Border = C("#2A3852"),
        Text = C("#F7F9FC"),
        Muted = C("#9BAAC2"),
        Accent = C("#7C9CFF"),
        AccentSoft = C("#243964"),
        Success = C("#53D69A"),
        Warning = C("#F5B94C"),
        Danger = C("#FF7B86"),
    };

    public static readonly Palette Light = new()
    {
        Canvas = C("#F4F7FB"),
        Surface = C("#FFFFFF"),
        SurfaceRaised = C("#F7F9FD"),
        Border = C("#E2E9F2"),
        Text = C("#172033"),
        Muted = C("#6D7A90"),
        Accent = C("#4F6EF7"),
        AccentSoft = C("#E9EEFF"),
        Success = C("#1FA971"),
        Warning = C("#C57A12"),
        Danger = C("#D94A59"),
    };

    public static Palette For(bool dark) => dark ? Dark : Light;

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
