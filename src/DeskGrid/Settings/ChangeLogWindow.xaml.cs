using System.Windows;
using System.Windows.Controls;
using DeskGrid.Resources;

namespace DeskGrid.Settings;

/// <summary>The change log of this version of DeskGrid and the ones before it, or only what is new since the last version the user saw.</summary>
internal partial class ChangeLogWindow : Window
{
    /// <summary>The whole change log (opened from Settings).</summary>
    public ChangeLogWindow()
    {
        InitializeComponent();
        FluentWindow.UseOpaqueBackground(this);
        Title = Strings.ChangeLogTitle;
        CloseButton.Content = Strings.ButtonClose;
        Fill(null, null);
    }

    /// <summary>"What's new": just the versions after <paramref name="seen"/>, up to <paramref name="current"/>, shown after an update.</summary>
    public ChangeLogWindow(Version? seen, Version current)
    {
        InitializeComponent();
        FluentWindow.UseOpaqueBackground(this);
        Title = string.Format(Strings.WhatsNewTitle, current.ToString(3));
        CloseButton.Content = Strings.ButtonClose;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; // No owner to center on.
        Fill(seen, current);
    }

    /// <summary>Whether there is anything to show for <paramref name="current"/> since <paramref name="seen"/> in the built-in change log.</summary>
    public static bool HasNews(Version? seen, Version current) =>
        ChangeLog.ReadEmbedded() is { } markdown && ChangeLog.Since(ChangeLog.Parse(markdown), seen, current).Count > 0;

    private void Fill(Version? seen, Version? current)
    {
        if (ChangeLog.ReadEmbedded() is not { } markdown)
        {
            Entries.Children.Add(new TextBlock { Text = Strings.ChangeLogMissing, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
            return;
        }

        var lines = ChangeLog.Parse(markdown);
        foreach (var line in current is null ? lines : ChangeLog.Since(lines, seen, current))
        {
            Entries.Children.Add(Render(line));
        }
    }

    private static UIElement Render(ChangeLogLine line) => line.Kind switch
    {
        ChangeLogKind.Release => new TextBlock
        {
            Text = line.Text,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 4),
        },
        ChangeLogKind.Section => new TextBlock
        {
            Text = line.Text,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.75,
            Margin = new Thickness(0, 8, 0, 2),
        },
        _ => Bullet(line.Text),
    };

    private static Grid Bullet(string text)
    {
        var grid = new Grid { Margin = new Thickness(8, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = "•" });

        var item = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(item, 1);
        grid.Children.Add(item);
        return grid;
    }
}
