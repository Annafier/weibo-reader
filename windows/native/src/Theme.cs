using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WeiboReader {
// Dark theme constants and shared brushes (matches the Eye-of-Sauron-style
// dark look: near-black surface, ember accent, cool blue for the B side).
public static class Theme {
    public static readonly Color BgColor = Color.FromRgb(0x0d, 0x0d, 0x10);
    public static readonly Color PanelColor = Color.FromRgb(0x14, 0x13, 0x17);
    public static readonly Color PanelBorder = Color.FromRgb(0x2a, 0x25, 0x23);
    public static readonly Color TextColor = Color.FromRgb(0xe8, 0xe2, 0xde);
    public static readonly Color MutedColor = Color.FromRgb(0x9a, 0x8b, 0x85);
    public static readonly Color AccentColor = Color.FromRgb(0xff, 0x6a, 0x3d);
    public static readonly Color AccentDim = Color.FromRgb(0xb0, 0x3a, 0x24);
    public static readonly Color BlueColor = Color.FromRgb(0x6a, 0xa6, 0xff);
    public static readonly Color DangerColor = Color.FromRgb(0xe0, 0x50, 0x40);
    public static readonly Color WarnColor = Color.FromRgb(0xc9, 0xa2, 0x27);
    public static readonly Color OkColor = Color.FromRgb(0x4a, 0xc0, 0x6a);

    public static readonly Brush Bg = new SolidColorBrush(BgColor);
    public static readonly Brush Panel = new SolidColorBrush(PanelColor);
    public static readonly Brush PanelBorderBrush = new SolidColorBrush(PanelBorder);
    public static readonly Brush Text = new SolidColorBrush(TextColor);
    public static readonly Brush Muted = new SolidColorBrush(MutedColor);
    public static readonly Brush Accent = new SolidColorBrush(AccentColor);
    public static readonly Brush AccentDimBrush = new SolidColorBrush(AccentDim);
    public static readonly Brush Blue = new SolidColorBrush(BlueColor);
    public static readonly Brush Danger = new SolidColorBrush(DangerColor);
    public static readonly Brush Warn = new SolidColorBrush(WarnColor);
    public static readonly Brush Ok = new SolidColorBrush(OkColor);
    public static readonly Brush Hover = new SolidColorBrush(Color.FromRgb(0x1e, 0x1c, 0x21));

    public static Style SidebarButtonStyle() {
        var style = new Style(typeof(System.Windows.Controls.Button));
        style.Setters.Add(new Setter(System.Windows.Controls.Button.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(System.Windows.Controls.Button.ForegroundProperty, Text));
        style.Setters.Add(new Setter(System.Windows.Controls.Button.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(System.Windows.Controls.Button.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(System.Windows.Controls.Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        style.Setters.Add(new Setter(System.Windows.Controls.Button.PaddingProperty, new Thickness(16, 10, 12, 10)));
        style.Setters.Add(new Setter(System.Windows.FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        return style;
    }
    public static Border Card() {
        return new Border {
            Background = Panel,
            BorderBrush = PanelBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12),
        };
    }
    public static TextBlock Label(string text) {
        return new TextBlock { Text = text, Foreground = Muted, FontSize = 12 };
    }
    public static TextBlock Title(string text) {
        return new TextBlock { Text = text, Foreground = Text, FontSize = 16, FontWeight = FontWeights.SemiBold };
    }
}
}
