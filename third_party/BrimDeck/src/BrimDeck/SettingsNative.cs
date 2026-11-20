using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BrimDeck;

// DeskGrid build (see DESKGRID.md): the settings pages look like the rest of DeskGrid's settings window, not like BrimDeck's own. The page
// code asks for BrimDeck's named styles (SettingsButton, SettingsToggle, SettingsCombo ...); for the ordinary controls those names now
// stand for the Fluent styles of the system's theme (the same ones DeskGrid's own pages get from the window's ThemeMode), with the few
// settings DeskGrid's XAML gives them (MinWidth 0 on a check box or radio button, and so on). What BrimDeck drew itself and has no
// ordinary control for (choice cards with a sketch, chips, steppers, clock tiles) keeps its template, in the neutral palette.
public sealed partial class SettingsPages
{
    private ResourceDictionary? _fluent;
    private readonly Dictionary<string, Style> _native = [];

    private ResourceDictionary Fluent => _fluent ??= new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml") };

    // The Fluent style of a control type, or null when this runtime has none.
    private Style? FluentStyle(Type type, string? key = null)
    {
        object lookup = key ?? (object)type;
        return Fluent.Contains(lookup) ? Fluent[lookup] as Style : null;
    }

    private Style Derived(string name, Type type, string? fluentKey, Action<Style> tweak)
    {
        if (_native.TryGetValue(name, out var cached)) return cached;
        var style = new Style(type, FluentStyle(type, fluentKey));
        tweak(style);
        return _native[name] = style;
    }

    private static void Set(Style style, DependencyProperty property, object value) => style.Setters.Add(new Setter(property, value));

    // A button as DeskGrid's own: Fluent's, without its minimum width (a button here is as wide as its text asks).
    private Style NativeButton(string name, bool accent = false) => Derived(name, typeof(Button), accent ? "AccentButtonStyle" : null, style =>
    {
        Set(style, FrameworkElement.MinWidthProperty, 0d);
    });

    // What the page code asks for under BrimDeck's own style names. Unknown names go to the resources as before.
    private new object FindResource(object key)
    {
        if (key is not string name) return base.FindResource(key);
        return name switch
        {
            "SettingsButton" or "SettingsOutlineButton" or "SettingsColorButton" or "SettingsJoinedButton" or "SettingsCardName"
                or "SettingsGlyphButton" or "SettingsCloseButton" or "SettingsCardDelete" or "SettingsDangerButton" => NativeButton(name),
            "SettingsPrimaryButton" => NativeButton(name, accent: true),
            "SettingsLink" => Derived(name, typeof(Button), null, style =>
            {
                Set(style, FrameworkElement.MinWidthProperty, 0d);
                Set(style, Control.BackgroundProperty, Brushes.Transparent);
                Set(style, Control.BorderThicknessProperty, new Thickness(0));
                Set(style, Control.PaddingProperty, new Thickness(4, 2, 4, 2));
                Set(style, Control.ForegroundProperty, new DynamicResourceExtension("AccentTextFillColorPrimaryBrush"));
            }),
            "SettingsToggle" => Derived(name, typeof(CheckBox), null, style => Set(style, FrameworkElement.MinWidthProperty, 0d)),
            "SettingsSegment" => Derived(name, typeof(RadioButton), null, style =>
            {
                Set(style, FrameworkElement.MinWidthProperty, 0d);
                Set(style, FrameworkElement.MarginProperty, new Thickness(0, 0, 16, 0));
            }),
            "SettingsCombo" => Derived(name, typeof(ComboBox), null, _ => { }),
            "SettingsField" or "SettingsNumber" => Derived(name, typeof(TextBox), null, _ => { }),
            "SettingsSlider" or "SettingsCenteredSlider" => Derived(name, typeof(Slider), null, _ => { }),
            "SettingsScrollViewer" or "SettingsPageScrollViewer" => Derived(name, typeof(ScrollViewer), null, _ => { }),
            _ => base.FindResource(key),
        };
    }
}
