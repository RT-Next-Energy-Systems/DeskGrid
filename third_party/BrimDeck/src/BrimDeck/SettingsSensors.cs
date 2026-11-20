using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;

namespace BrimDeck;

// DeskGrid build: the "enhanced sensors" page (a PawnIO driver and an administrator service) is not part of this copy of
// BrimDeck. Only what other pages still use remains: the page number (left out of PageOrder, so it is never shown) and
// the inline link helper.
public sealed partial class SettingsPages
{
    internal const int SensorsPage = 10;
    private Action? _refreshSensors;

    private void SensorsSettings() { }

    // A link inside a line of text: the accent color, underlined under the pointer or with keyboard focus.
    private Hyperlink InlineLink(string text, string url)
    {
        var link = new Hyperlink(new Run(text)) { Foreground = UI.Brush(_palette.Link), TextDecorations = null, ToolTip = url, Cursor = System.Windows.Input.Cursors.Hand };
        void Underline() => link.TextDecorations = link.IsMouseOver || link.IsKeyboardFocused ? TextDecorations.Underline : null;
        link.MouseEnter += (_, _) => Underline(); link.MouseLeave += (_, _) => Underline();
        link.GotKeyboardFocus += (_, _) => Underline(); link.LostKeyboardFocus += (_, _) => Underline();
        link.Click += (_, _) => UI.OpenUrl(url);
        AutomationProperties.SetName(link, text.TrimEnd(' ', '↗'));
        return link;
    }
}
