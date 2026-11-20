using BrimDeck.Core;

namespace BrimDeck;

// Explains Apple Music's full control before it is enabled: what it adds, how it works, and what it does to the
// Apple Music window (ControlDialog).
internal sealed class AppleMusicControlWindow : ControlDialog
{
    public AppleMusicControlWindow(SettingsTheme theme) : base(theme, Loc.T("啟用 Apple Music 完整控制", "Enable full control of Apple Music"))
    {
        Add(Loc.T("Apple Music 只向 Windows 提供播放、暫停和切換歌曲的控制。啟用完整控制後，BrimDeck 會透過 Windows 的介面自動化功能，直接操作 Apple Music 視窗中的按鈕和進度條，在面板中增加以下三項功能：",
            "Apple Music only lets Windows play, pause and change songs. With full control, BrimDeck uses the UI Automation feature of Windows to operate the buttons and the progress bar in the Apple Music window directly, and adds three functions to the panel:"), 10);
        Add(Loc.T("•  拖動播放進度\n•  開啟或關閉隨機播放\n•  切換重複模式：關閉、全部重複、單曲重複",
            "•  Drag the playback position\n•  Turn shuffle on or off\n•  Switch the repeat mode: off, repeat all, repeat one"), 8, color: Palette.Primary);

        Add(Loc.T("使用前請了解", "Before you enable it"), 22, 13.5, Palette.Primary, true);
        Add(Loc.T("•  如果 Apple Music 的主視窗沒有最小化，使用這三項功能時，它的視窗會被帶到最前面。\n•  這三項功能需要 Apple Music 的主視窗存在。關閉主視窗會退出 Apple Music，功能也隨之不可用。\n•  Apple Music 更新後，如果視窗中的按鈕發生變化，這三項功能可能暫時無法使用。",
            "•  If the Apple Music main window is not minimized, using these functions brings it to the front.\n•  These functions need the Apple Music main window. Closing it quits Apple Music, and the functions stop working.\n•  If an Apple Music update changes the buttons in its window, these functions may stop working for a while."), 6);

        Finish(Loc.T("啟用", "Enable"));
    }
}
