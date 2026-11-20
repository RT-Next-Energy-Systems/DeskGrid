using System.Windows;
using System.Windows.Controls;
using BrimDeck.Core;

namespace BrimDeck;

// Mouse pass-through (DeckSettings.ClickThrough) in the General page's Compatibility group: its switch, and under it, while it is on, whether the open
// panel takes clicks (ClickThroughPanel) and the keyboard (ClickThroughKeyboard). Its shortcut and the tray menu also turn it on and off; the switch follows them.
public sealed partial class SettingsWindow
{
    private CheckBox? _clickThroughBox;

    private FrameworkElement ClickThroughRows()
    {
        var clickable = Toggle(Loc.T("展開後的面板可以點選", "Expanded panel takes clicks"), S.ClickThroughPanel, value => Change(s => s.ClickThroughPanel = value),
            help: Loc.T("開啟後，展開的面板可以正常點選，滑鼠進入再離開後收起。關閉時，面板不接收滑鼠，一直保持展開，直到再按展開快捷鍵收起。",
                "When on, the expanded panel takes clicks as usual and collapses once the pointer has entered and left it. When off, it takes no mouse input and stays open until the expand shortcut collapses it."));
        var keyboard = Toggle(Loc.T("面板接收鍵盤", "Panel takes keyboard input"), S.ClickThroughKeyboard, value => Change(s => s.ClickThroughKeyboard = value),
            help: Loc.T("輔助操作，與 Windows 中的鍵盤操作相同。開啟後，用快捷鍵展開的面板可以用 Tab、方向鍵、空格、回車操作，按 Esc 收起，這期間遊戲收不到這些按鍵。",
                "An assistive option that works like Windows keyboard navigation. When on, a panel expanded by a shortcut takes Tab, the arrow keys, Space and Enter, and Esc collapses it. Your game does not get these keys meanwhile."));
        var more = new StackPanel { Visibility = S.ClickThrough ? Visibility.Visible : Visibility.Collapsed };
        more.Children.Add(new Border { Height = 1, Background = UI.Brush(_palette.RowDivider) });
        more.Children.Add(clickable);
        more.Children.Add(new Border { Height = 1, Background = UI.Brush(_palette.RowDivider) });
        more.Children.Add(keyboard);
        var main = Toggle(Loc.T("滑鼠穿透模式", "Mouse pass-through"), S.ClickThrough, value =>
        {
            if (S.ClickThrough != value) Change(s => s.ClickThrough = value);
            more.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }, help: Loc.T("開啟後，滑鼠經過不會展開面板，點選會直接落到下方視窗，適合玩遊戲時使用。快捷鍵在“設定 › 快捷鍵”中設定。",
            "When on, the pointer passing over does not expand the panel and clicks go to the window beneath, which suits games. Set shortcuts in Settings › Shortcuts."));
        _clickThroughBox = ((Panel)main).Children.OfType<CheckBox>().FirstOrDefault();
        var group = new StackPanel();
        group.Children.Add(main); group.Children.Add(more);
        return group;
    }

    // The shortcut or the tray menu turned pass-through on or off.
    internal void ClickThroughChanged()
    {
        if (_clickThroughBox is { IsLoaded: true } box && box.IsChecked != S.ClickThrough) box.IsChecked = S.ClickThrough;
    }
}
