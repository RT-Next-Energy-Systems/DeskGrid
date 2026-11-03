# Project Summary

## Project Name
DeskGrid（repo 目錄名 DeskGrid）

## Project Goal
在 Windows 10 22H2 / 11 桌面放置類似 iOS/Android 的資料夾 widget：2×2 縮圖預覽、點擊展開格子面板、每個 widget 對應一個實體資料夾並即時同步。MIT 開源。

## Project Description
見 `CLAUDE.md` 開頭與「行為規則」。

## Architecture
見 `CLAUDE.md`「架構」與各階段「實作要點」（唯一來源，此處不重複）。

## Tech Stack
C# + WPF、.NET 10 LTS、x64、CsWin32、xUnit v3、self-contained 單一 exe。詳見 `CLAUDE.md`「技術棧」。

## Directory Structure
```
DeskGrid.slnx          solution
Directory.Build.props     shared settings (nullable, x64, warnings as errors)
global.json               SDK 10.0.x (rollForward latestFeature)
.github/workflows/        ci.yml (build/test/publish), release.yml (v* tag → GitHub Release)
README.md, README.zh-TW.md, LICENSE (MIT)
src/DeskGrid/
  DeskGrid.csproj
  app.manifest            PerMonitorV2, asInvoker
  NativeMethods.txt       CsWin32 API list
  App.xaml(.cs)           entry; ShutdownMode=OnExplicitShutdown
  Config/                 AppConfig, ConfigStore
  Assets/                 DeskGrid.ico (tools/make-icon.ps1)
  Desktop/                DesktopHost, DesktopSurface, IconLayer, MonitorArea, ShellMessageWindow, ScreenPoint/ScreenRect
  Diagnostics/            Log
  Folders/                FolderScanner, WidgetCatalog, RootFolder, FolderPaths, ItemImporter, FolderDeleter, DesktopFiles, WidgetFolderName, NaturalStringComparer, records
  Icons/                  IconService, PreviewComposer
  Panel/                  PopupPanel (XAML), PanelController, PanelPlacement, PanelTheme, Backdrop, PanelItem
  Resources/              Strings.resx (en), Strings.zh-TW.resx
  Settings/               SettingsWindow (XAML), NewFolderDialog (XAML), ColorDialog
  Shell/                  ShellImage, ShellItem, ShortcutFile, RecycleBin
  Threading/              DispatcherThread (STA worker)
  Tray/                   TrayIcon, AutoStart
  Widgets/                FolderWidget (XAML), WidgetBoard, WidgetGrid, IconMerger
tests/DeskGrid.Tests/  xUnit v3
tools/                    make-icon.ps1
context/                  session context docs
```

## Core Modules
DesktopHost（`Desktop/`）、ConfigStore（`Config/`）、FolderScanner + WidgetCatalog（`Folders/`）、IconService（`Icons/`）、FolderWidget + WidgetBoard（`Widgets/`）、PopupPanel + PanelController（`Panel/`）、TrayIcon + AutoStart（`Tray/`）、設定（`Settings/`）、拖放（`Folders/ItemImporter`）。其餘模組見 `CLAUDE.md`「架構」。

## Coding Rules
見 `AGENTS.md`.

## Current Milestone
階段 7：CI、打包、README、LICENSE（MVP 最後一個階段）。

## Current Status
階段 1–6 使用者驗證通過。階段 7 完成於本機：單一 exe 打包並實際執行成功、CI 與 Release workflow、README（en / zh-TW）、MIT LICENSE；待推上公開 repo `Ryan970714/DeskGrid` 並確認 CI 通過。

## Known Issues
- 舊版桌面階層（Win10 / Win11 ≤ 23H2）程式路徑尚未實機測試。
- 落到 widget 上的桌面圖示會被移到最近的空格（開「自動排列圖示」時改由 widget 讓開）；DeskGrid 沒在執行時放上去的圖示，啟動時由 widget 避開而不是移圖示。
- 關閉「顯示桌面圖示」時 widget 仍會顯示，尚未決定是否要跟著隱藏。
- 跨行程掛子視窗會連結 input queue：Explorer 卡住時我們的 UI 執行緒可能一起卡住。`0x052C` 最多阻塞 1 秒。
- 位置/大小以系統 DPI 計算；執行中改 DPI、多螢幕尚未處理（MVP 範圍外）。
- 程式關閉期間改名的資料夾/項目無法轉移設定（下次啟動視為刪除 + 新增）。
- 無法讀取的 widget 資料夾（權限不足）在該次掃描中視為不存在，其設定會被清除。
- 執行中刪除根目錄本身：watcher 停止，不會自動恢復（重啟 app 會重建）。
- 捷徑目標程式更新圖示但 .lnk 本身沒變時，快取的舊圖示要到重啟 app 才更新。
- 圖示快取只在記憶體，每次啟動重新向 Shell 取（每個項目一次）。
- 只有小尺寸圖示的程式在 256px 時由 Shell 放大，可能偏糊（本機樣本都有 256px 圖示）。- 執行中改解析度、DPI、工作列位置時 widget 不會重新吸附，重啟 app 才夾回工作區。
- 多螢幕：格子大小依各螢幕 DPI 算，但 widget 實際以 Progman 的 DPI 渲染，不同 DPI 的副螢幕上可能錯位。
- 拖曳中無法用 Esc 取消；放到已佔用的格子會被放到次近的空格。
- 面板開著時拖曳另一個 widget，面板不會自動關閉。
- 面板背景只在開啟時讀取設定與系統深淺色。
- 從開始功能表拖 App（UWP / 非檔案系統項目）到 widget 不支援（沒有檔案路徑）。
- 以系統管理員身分執行時，UIPI 會擋掉從檔案總管拖來的檔案。
- 系統匣選單是原生 Win32 選單，深色模式下仍是淺色；設定視窗為傳統 WPF 外觀（淺色）。
- 變更根目錄會重建所有 widget（短暫閃爍）。
- Win10 上「system」背景走不透明系統色路徑，未在 Win10 實機看過。
- 面板內只支援 Tab / Enter 鍵盤操作，沒有方向鍵。

## Future Plan
依 `CLAUDE.md`「開發階段與進度」3 → 7。

---
*本檔案應保持精簡（1–3 頁），僅記錄長期不變或緩慢變動的專案事實。工作日誌/進度細節見 `current-task.md`、`progress.md`、`session-log.md`。*
