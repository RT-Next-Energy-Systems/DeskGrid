using BrimDeck.Core;

namespace BrimDeck;

// Asks before NetEase Cloud Music's libcef.dll is changed (NeteaseInstall): what it adds, what happens to the player, and the
// debugging port, in a few lines; the details are in the user documentation (ControlDialog).
internal sealed class NeteaseFileControlWindow : ControlDialog
{
    public NeteaseFileControlWindow(SettingsTheme theme) : base(theme, Loc.T("啟用網易雲完整控制", "Turn on NetEase Cloud Music full control"))
    {
        Add(Loc.T("啟用後，可以在面板裡拖動網易雲的播放進度、切換隨機與迴圈播放，進度也會實時同步。",
            "When on, you can drag NetEase Cloud Music's playback position in the panel and switch shuffle and repeat, and the position stays in sync."), 10);
        Add(Loc.T("•  網易雲正在執行時會重啟一次，然後回到當前這首歌和播放位置", "•  A running player restarts once, then returns to the current song and position"), 10, color: Palette.Primary);
        Add(Loc.T("•  之後網易雲每次啟動都是完整控制，不必再逐次啟用", "•  NetEase Cloud Music then starts with full control every time, with nothing to enable again"), 10, color: Palette.Primary);
        AddFileChange(Loc.T("•  會修改網易雲的 libcef.dll", "•  NetEase Cloud Music's libcef.dll is changed"),
            Loc.T("只替換檔案中 2 處原有的位元組（共約 12 位元組），讓網易雲啟動時開啟除錯埠。不新增內容，檔案大小不變；停用時寫回原來的位元組。網易雲更新後若還原了檔案，會自動再次修改。",
                "Only 2 existing places in the file are replaced (about 12 bytes in all), so that NetEase Cloud Music opens its debugging port when it starts. Nothing is added and the file keeps its size; turning it off writes the original bytes back. If an update restores the file, it is changed again."));

        var notice = AddNotice(Loc.T("只在您信任的電腦上啟用", "Turn it on only on a PC you trust"));
        Add(NeteaseControlWindow.SecurityNotice + Loc.T("因為檔案已修改，網易雲每次啟動都會開啟除錯埠，直到關閉此開關。",
            " Because the file is changed, NetEase Cloud Music opens the port every time it starts, until this switch is turned off."), 6, into: notice);

        Finish(Loc.T("啟用", "Turn on"));
    }
}
