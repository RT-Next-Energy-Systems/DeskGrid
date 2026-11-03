using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using DeskGrid.Resources;
using DeskGrid.Shell;
using Windows.Win32;

namespace DeskGrid.Panel;

/// <summary>
/// The short menu Windows 11 shows first for a file: Cut, Copy and Delete as a row of icons, then Open and
/// Properties, then "Show more options" for the full (classic) menu. Windows has no public API for its own, so this is
/// a WPF copy that takes the commands from the classic menu and runs them through it.
/// </summary>
internal static class CompactShellMenu
{
    private const int FirstWindows11Build = 22000;
    private const int ShiftKey = 0x10; // VK_SHIFT

    // The icon row, in order, with their glyphs in Segoe Fluent Icons (Segoe MDL2 Assets before Windows 11).
    private static readonly (string Verb, string Glyph)[] IconRow = [("cut", ""), ("copy", ""), ("delete", "")];

    private static readonly string[] TopVerbs = ["open", "runas"];
    private static readonly string[] BottomVerbs = ["properties"];

    // A button without a frame that shades in the text color when the mouse is over it.
    private const string IconButtonStyle = """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Setter Property="Width" Value="40" />
          <Setter Property="Height" Value="36" />
          <Setter Property="Focusable" Value="False" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Grid Background="Transparent">
                  <Border x:Name="Shade" CornerRadius="4" Opacity="0"
                          Background="{Binding Foreground, RelativeSource={RelativeSource TemplatedParent}}" />
                  <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Shade" Property="Opacity" Value="0.12" />
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """;

    // The row is not an item to choose: no highlight, just its content.
    private const string RowTemplate = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="MenuItem">
          <ContentPresenter ContentSource="Header" />
        </ControlTemplate>
        """;

    /// <summary>Whether this system and this click call for the short menu: Windows 11, and Shift not held.</summary>
    public static bool IsWanted => Environment.OSVersion.Version.Build >= FirstWindows11Build
        && (PInvoke.GetAsyncKeyState(ShiftKey) & 0x8000) == 0;

    /// <summary>
    /// Builds the short menu for <paramref name="shell"/>, or returns <see langword="null"/> when it would have
    /// nothing to offer (the classic menu is then shown). <paramref name="choose"/> gets the command picked and
    /// <paramref name="chooseMore"/> is called for "Show more options"; both are called before the menu closes.
    /// </summary>
    public static ContextMenu? Build(ShellContextMenu shell, Action<ShellContextMenu.Entry> choose, Action chooseMore)
    {
        var entries = shell.ReadEntries().GroupBy(entry => entry.Verb, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var menu = new ContextMenu();

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (verb, glyph) in IconRow)
        {
            if (entries.TryGetValue(verb, out var entry))
            {
                var button = new Button
                {
                    Style = (Style)XamlReader.Parse(IconButtonStyle),
                    ToolTip = entry.Text,
                    Content = new TextBlock
                    {
                        Text = glyph,
                        FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                        FontSize = 16,
                    },
                };
                button.Click += (_, _) =>
                {
                    choose(entry);
                    menu.IsOpen = false;
                };
                row.Children.Add(button);
            }
        }

        if (row.Children.Count > 0)
        {
            menu.Items.Add(new MenuItem
            {
                Header = row,
                Template = (ControlTemplate)XamlReader.Parse(RowTemplate),
                Focusable = false,
            });
            menu.Items.Add(new Separator());
        }

        var any = AddCommands(menu, entries, TopVerbs, choose);
        if (any)
        {
            menu.Items.Add(new Separator());
        }

        any |= AddCommands(menu, entries, BottomVerbs, choose);
        if (!any && row.Children.Count == 0)
        {
            return null;
        }

        var more = new MenuItem { Header = Strings.PanelMoreOptions };
        more.Click += (_, _) => chooseMore();
        menu.Items.Add(more);
        return menu;
    }

    private static bool AddCommands(
        ContextMenu menu,
        Dictionary<string, ShellContextMenu.Entry> entries,
        string[] verbs,
        Action<ShellContextMenu.Entry> choose)
    {
        var added = false;
        foreach (var verb in verbs)
        {
            if (entries.TryGetValue(verb, out var entry))
            {
                var item = new MenuItem { Header = entry.Text };
                item.Click += (_, _) => choose(entry);
                menu.Items.Add(item);
                added = true;
            }
        }

        return added;
    }
}
