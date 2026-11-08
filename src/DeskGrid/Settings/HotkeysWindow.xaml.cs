using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskGrid.Config;
using DeskGrid.Hotkeys;
using DeskGrid.Resources;

namespace DeskGrid.Settings;

/// <summary>What the user set for one action in <see cref="HotkeysWindow"/> (<see langword="null"/>: no shortcut).</summary>
internal sealed record HotkeyChoice(string Id, Hotkey? Key);

/// <summary>
/// Lists the actions that can have a keyboard shortcut (the general ones, then one per folder) with a box for each.
/// Edits a copy: <see cref="Applied"/> fires on OK with every action's shortcut. The shortcuts in force should be
/// switched off while this is open, or Windows would take the keys being recorded.
/// </summary>
internal partial class HotkeysWindow : Window
{
    private readonly List<(string Id, string Label, HotkeyBox Box)> _rows = [];

    public HotkeysWindow(AppConfig config, IReadOnlyList<string> folderNames)
    {
        InitializeComponent();
        FluentWindow.UseOpaqueBackground(this);
        MaxHeight = SystemParameters.WorkArea.Height;
        Title = Strings.HotkeysTitle;
        Intro.Text = Strings.HotkeysIntro;
        ResetButton.Content = Strings.HotkeysReset;
        OkButton.Content = Strings.ButtonOk;
        CancelButton.Content = Strings.ButtonCancel;

        AddGroup(Strings.HotkeysGroupGeneral, HotkeyBindings.GeneralIds.Select(id => (id, LabelOf(id))), config);
        AddGroup(Strings.HotkeysGroupFolders, folderNames.Select(name => (HotkeyBindings.FolderId(name), name)), config);
    }

    public event EventHandler<IReadOnlyList<HotkeyChoice>>? Applied;

    /// <summary>The box of the action <paramref name="id"/> (for tests).</summary>
    internal HotkeyBox BoxFor(string id) => _rows.First(row => row.Id == id).Box;

    /// <summary>What an action is called in the list: the folder's name, or the general action's text.</summary>
    public static string LabelOf(string id) => HotkeyBindings.FolderNameOf(id) ?? id switch
    {
        HotkeyBindings.AllFolders => Strings.HotkeyAllFolders,
        HotkeyBindings.NewFolder => Strings.HotkeyNewFolder,
        _ => Strings.HotkeySettings,
    };

    private void AddGroup(string title, IEnumerable<(string Id, string Label)> actions, AppConfig config)
    {
        var rows = new StackPanel();
        foreach (var (id, label) in actions)
        {
            rows.Children.Add(MakeRow(id, label, HotkeyBindings.Get(config, id)));
        }

        if (rows.Children.Count == 0)
        {
            rows.Children.Add(new TextBlock { Text = Strings.HotkeysNoFolders, Opacity = 0.75 });
        }

        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("CardTitle") });
        content.Children.Add(rows);
        Groups.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = content });
    }

    private Grid MakeRow(string id, string label, Hotkey? key)
    {
        var box = new HotkeyBox { Value = key, Width = 170 };
        box.Check = candidate => _rows.FirstOrDefault(row => row.Box != box && row.Box.Value == candidate) is { Label: { } other }
            ? string.Format(Strings.HotkeysInUse, candidate, other)
            : null;
        box.Rejected += (_, problem) => ShowError(problem);
        box.ValueChanged += (_, _) => Error.Visibility = Visibility.Collapsed;

        var clear = new Button
        {
            Content = "", // Cancel (x) in Segoe MDL2 Assets
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            IsTabStop = false, // Backspace clears the focused box.
            ToolTip = Strings.HotkeysClear,
        };
        clear.Click += (_, _) =>
        {
            box.Value = null;
            Error.Visibility = Visibility.Collapsed;
        };

        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(box, 1);
        Grid.SetColumn(clear, 2);
        row.Children.Add(name);
        row.Children.Add(box);
        row.Children.Add(clear);

        _rows.Add((id, label, box));
        return row;
    }

    private void ShowError(string text)
    {
        Error.Text = text;
        Error.Visibility = Visibility.Visible;
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        foreach (var (id, _, box) in _rows)
        {
            box.Value = HotkeyBindings.DefaultOf(id);
        }

        Error.Visibility = Visibility.Collapsed;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Applied?.Invoke(this, [.. _rows.Select(row => new HotkeyChoice(row.Id, row.Box.Value))]);
        Close();
    }
}
