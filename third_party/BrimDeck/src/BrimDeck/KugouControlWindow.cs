using BrimDeck.Core;

namespace BrimDeck;

// Asks before KuGou's libcef.dll is changed (KugouInstall): what it adds, what happens to KuGou, and the debugging port, in a
// few lines; the details are in the user documentation (ControlDialog).
internal sealed class KugouControlWindow : ControlDialog
{
    public KugouControlWindow(SettingsTheme theme) : base(theme, Loc.T("啟用酷狗完整控制", "Turn on KuGou full control"))
    {
        Add(Loc.T("啟用後，面板顯示酷狗的播放進度，歌詞隨進度滾動。", "When on, the panel shows KuGou's position and scrolls the lyrics with it."), 10);
        Add(Loc.T("•  酷狗正在執行時會重啟一次，正在播放的歌曲會停止", "•  A running KuGou restarts once, and the playing song stops"), 10, color: Palette.Primary);
        AddFileChange(Loc.T("•  會修改酷狗的 libcef.dll，之後酷狗每次啟動都是完整控制", "•  KuGou's libcef.dll is changed; KuGou then starts with full control every time"),
            Loc.T("只替換檔案中 9 處原有的位元組（共約 50 位元組），讓酷狗啟動時開啟除錯埠。不新增內容，檔案大小不變；停用時寫回原來的位元組。",
                "Only 9 existing places in the file are replaced (about 50 bytes in all), so that KuGou opens its debugging port when it starts. Nothing is added and the file keeps its size; turning it off writes the original bytes back."));

        var notice = AddNotice(Loc.T("只在您信任的電腦上啟用", "Turn it on only on a PC you trust"));
        Add(Loc.T("酷狗會開啟一個本機除錯埠，其他程式可能借此操控酷狗。", "KuGou opens a local debugging port that other programs could use to control it."), 6, into: notice);

        Finish(Loc.T("啟用", "Turn on"));
    }
}
