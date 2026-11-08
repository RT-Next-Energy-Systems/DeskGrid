using System.IO;
using System.Runtime.CompilerServices;
using DeskGrid.Diagnostics;

namespace DeskGrid.Tests;

internal static class TestSetup
{
    [ModuleInitializer]
    internal static void RedirectLog() =>
        Log.FilePath = Path.Combine(Path.GetTempPath(), "DeskGrid.Tests", "test.log");
}

/// <summary>A unique, auto-deleted directory under %TEMP%.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "DeskGrid.Tests", Guid.NewGuid().ToString("N"));

    public string Combine(string relativePath) => System.IO.Path.Combine(Path, relativePath);

    /// <summary>Creates an empty file (and its folders) at <paramref name="relativePath"/>.</summary>
    public string CreateFile(string relativePath, FileAttributes attributes = FileAttributes.Normal)
    {
        var path = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        File.SetAttributes(path, attributes);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A watcher may still hold a handle; %TEMP% gets cleaned eventually.
        }
    }
}

/// <summary>The WPF application the windows under test find their styles in.</summary>
internal static class TestApplication
{
    /// <summary>Creates it on the calling (UI) thread if there is none yet. The windows take their card styles from App.xaml; the card's padding matters for what fits in a row.</summary>
    public static void Ensure()
    {
        if (System.Windows.Application.Current is not null)
        {
            return;
        }

        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        var card = new System.Windows.Style(typeof(System.Windows.Controls.Border));
        card.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Border.PaddingProperty, new System.Windows.Thickness(16, 14, 16, 14)));
        card.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Border.BorderThicknessProperty, new System.Windows.Thickness(1)));
        card.Setters.Add(new System.Windows.Setter(System.Windows.FrameworkElement.MarginProperty, new System.Windows.Thickness(0, 0, 0, 8)));
        app.Resources["Card"] = card;
        app.Resources["CardTitle"] = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
    }
}
