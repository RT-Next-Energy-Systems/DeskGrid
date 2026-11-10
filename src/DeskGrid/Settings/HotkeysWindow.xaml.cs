using System.Windows;
using System.Windows.Automation;
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
    private readonly List<Action> _counters = [];

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

        // DeskGrid itself first (including "open all folders"), then NotiLand, then one row per folder.
        AddGroup(Strings.HotkeysGroupGeneral, [.. HotkeyBindings.AppIds.Select(id => (id, LabelOf(id)))], config);
        AddGroup(Strings.HotkeysGroupIsland, [.. HotkeyBindings.IslandIds.Select(id => (id, LabelOf(id)))], config, hint: Strings.HotkeysIslandHint);
        AddGroup(Strings.HotkeysGroupFolders, [.. folderNames.Select(name => (HotkeyBindings.FolderId(name), name))], config,
            note: folderNames.Count == 0 ? Strings.HotkeysNoFolders : null);
    }

    public event EventHandler<IReadOnlyList<HotkeyChoice>>? Applied;

    /// <summary>The box of the action <paramref name="id"/> (for tests).</summary>
    internal HotkeyBox BoxFor(string id) => _rows.First(row => row.Id == id).Box;

    /// <summary>What an action is called in the list: the folder's name, or the general action's text.</summary>
    public static string LabelOf(string id) => HotkeyBindings.FolderNameOf(id) ?? id switch
    {
        HotkeyBindings.AllFolders => Strings.HotkeyAllFolders,
        HotkeyBindings.IslandToggle => Strings.HotkeyIslandToggle,
        HotkeyBindings.IslandTimer => Strings.HotkeyIslandTimer,
        HotkeyBindings.IslandCard => Strings.HotkeyIslandCard,
        HotkeyBindings.NewFolder => Strings.HotkeyNewFolder,
        _ => Strings.HotkeySettings,
    };

    // A card per group: its title with how many of its actions have a shortcut (kept up to date while editing), an optional
    // hint under the title, a row per action, and an optional dim note under the rows (e.g. "No folders yet").
    private void AddGroup(string title, IReadOnlyList<(string Id, string Label)> actions, AppConfig config, string? hint = null, string? note = null)
    {
        var rows = new StackPanel();
        var boxes = new List<HotkeyBox>();
        foreach (var (id, label) in actions)
        {
            rows.Children.Add(MakeRow(id, label, HotkeyBindings.Get(config, id)));
            boxes.Add(_rows[^1].Box);
        }

        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6, FontSize = 12 };
        void UpdateCount() => count.Text = string.Format(Strings.HotkeysGroupCount, boxes.Count(box => box.Value is not null), boxes.Count);
        UpdateCount();
        _counters.Add(UpdateCount);
        foreach (var box in boxes)
        {
            box.ValueChanged += (_, _) => UpdateCount();
        }

        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(count, Dock.Right);
        heading.Children.Add(count);
        heading.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("CardTitle"), VerticalAlignment = VerticalAlignment.Center });

        var content = new StackPanel();
        content.Children.Add(heading);
        if (hint is not null)
        {
            content.Children.Add(new TextBlock { Text = hint, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        }

        content.Children.Add(rows);
        if (note is not null)
        {
            content.Children.Add(new TextBlock { Text = note, Opacity = 0.7, Margin = new Thickness(0, 6, 0, 0) });
        }

        Groups.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = content });
    }

    private Grid MakeRow(string id, string label, Hotkey? key)
    {
        var box = new HotkeyBox { Value = key, Width = 170 };
        box.Check = candidate => _rows.FirstOrDefault(row => row.Box != box && row.Box.Value == candidate) is { Label: { } other }
            ? string.Format(Strings.HotkeysInUse, candidate, other)
            : null;
        AutomationProperties.SetName(box, label); // What a screen reader says for the box: the action it sets.
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
        AutomationProperties.SetName(clear, $"{Strings.HotkeysClear}: {label}");
        clear.Click += (_, _) =>
        {
            box.Value = null;
            RefreshCounts();
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

    /// <summary>Brings the "n of m set" of every group up to date (after a box was set in code: cleared, reset).</summary>
    internal void RefreshCounts() => _counters.ForEach(update => update());

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

        RefreshCounts();

        Error.Visibility = Visibility.Collapsed;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Applied?.Invoke(this, [.. _rows.Select(row => new HotkeyChoice(row.Id, row.Box.Value))]);
        Close();
    }
}
