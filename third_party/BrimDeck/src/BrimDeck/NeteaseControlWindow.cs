using BrimDeck.Core;

namespace BrimDeck;

// What confirming does: restart a player in normal mode, start one that is not running,
// restart one that already has full control so the connection is made again, or switch on
// the setting that restarts every player started the normal way.
internal enum NeteaseControlAction { Restart, Start, Reconnect, Auto }

// Asks before NetEase Cloud Music is restarted with its debugging port (ControlDialog).
internal sealed class NeteaseControlWindow : ControlDialog
{
    internal static string SecurityNotice => Loc.T("完整控制依賴網易雲的除錯埠。埠開啟期間，這臺電腦上的其他程式和瀏覽器網頁有可能借此操控網易雲，讀取它顯示的內容和登入狀態。埠會一直開到網易雲以普通方式重新啟動為止，退出 BrimDeck 或連線失敗都不會關閉它。",
        "Full control relies on the debugging port of NetEase Cloud Music. While the port is open, other programs and web pages on this PC could use it to control NetEase Cloud Music and read what it shows and its sign-in state. The port stays open until NetEase Cloud Music is started again the normal way; quitting BrimDeck or a failed connection does not close it.");

    public NeteaseControlWindow(SettingsTheme theme, NeteaseControlAction action = NeteaseControlAction.Restart)
        : base(theme, action == NeteaseControlAction.Auto ? Loc.T("自動啟用網易雲完整控制", "Enable full control of NetEase Cloud Music automatically") : Loc.T("啟用網易雲完整控制", "Enable full control of NetEase Cloud Music"))
    {
        bool auto = action == NeteaseControlAction.Auto, start = action == NeteaseControlAction.Start;
        Add(action switch
        {
            NeteaseControlAction.Start => Loc.T("啟用完整控制後，可以在面板裡拖動網易雲音樂的進度、切換隨機與迴圈播放，進度也會實時同步。",
                "With full control, you can drag the playback position of NetEase Cloud Music in the panel and switch shuffle and repeat, and the position stays in sync."),
            NeteaseControlAction.Auto => Loc.T("開啟後，每當網易雲音樂以普通方式啟動，BrimDeck 會自動將它重啟為完整控制。完整控制下，可以在面板裡拖動進度、切換隨機與迴圈播放，進度也會實時同步。",
                "Once this is on, whenever NetEase Cloud Music is started the normal way, BrimDeck restarts it with full control. With full control, you can drag the playback position in the panel and switch shuffle and repeat, and the position stays in sync."),
            NeteaseControlAction.Reconnect => Loc.T("網易雲音樂已處於完整控制。繼續操作會重新啟動網易雲音樂，並重新建立連線。",
                "NetEase Cloud Music already has full control. Continuing restarts NetEase Cloud Music and connects to it again."),
            _ => Loc.T("BrimDeck 現在只能讀取網易雲正在播放的內容。啟用完整控制後，可以在面板裡拖動進度、切換隨機與迴圈播放，進度也會實時同步。",
                "BrimDeck can now only read what NetEase Cloud Music is playing. With full control, you can drag the playback position in the panel and switch shuffle and repeat, and the position stays in sync.")
        }, 10);

        Add(auto ? Loc.T("網易雲每次啟動後會重啟一次", "NetEase Cloud Music restarts once each time it starts")
            : start ? Loc.T("會啟動網易雲音樂", "NetEase Cloud Music will start") : Loc.T("會重啟一次網易雲", "NetEase Cloud Music will restart once"), 22, 13.5, Palette.Primary, true);
        Add(auto ? Loc.T("網易雲的視窗會關閉再開啟，並回到原來的歌曲和播放位置，通常需要幾秒鐘。網易雲如果此刻正以普通方式執行，點選“開啟”後會立即重啟一次。",
                "Its window closes and opens again and returns to the same song and position, which usually takes a few seconds. If NetEase Cloud Music is running the normal way now, it restarts once as soon as you click \"Turn on\".")
            : start ? Loc.T("點選“啟動並啟用”後，網易雲音樂會以完整控制方式開啟。整個過程通常需要幾秒鐘。",
                "After you click \"Start and enable\", NetEase Cloud Music opens with full control. This usually takes a few seconds.")
            : Loc.T("點選“重啟並啟用”後，網易雲音樂會先關閉再重新開啟，然後自動回到當前這首歌和播放位置；如果此刻正在播放，會接著播放。整個過程通常需要幾秒鐘。",
                "After you click \"Restart and enable\", NetEase Cloud Music closes and opens again, then returns to the current song and position; if it is playing now, it keeps playing. This usually takes a few seconds."), 6);

        var notice = AddNotice(Loc.T("只在您信任的電腦上啟用", "Enable it only on a PC you trust"));
        Add(auto ? SecurityNotice + Loc.T("開啟此開關後，網易雲每次啟動都會開啟除錯埠，直到關閉此開關。",
            " With this setting on, the port is opened each time NetEase Cloud Music starts, until the setting is turned off.") : SecurityNotice, 6, into: notice);

        Finish(auto ? Loc.T("開啟", "Turn on") : start ? Loc.T("啟動並啟用", "Start and enable") : Loc.T("重啟並啟用", "Restart and enable"));
    }
}
