# DeskGrid

Windows 10 (22H2) / 11 桌面上的「手機式資料夾」：資料夾圖示顯示內部 App 的 2×2 縮圖預覽，點擊展開格子面板，點外部或 Esc 收合。每個 widget 對應一個實體資料夾，改資料夾內容即同步。之後以 MIT 開源，程式碼需整潔、可維護。

> 本檔與 `CLAUDE.md`（英文）互為雙語版，修改時兩份一起改。見 `AGENTS.md`「語言政策」。

# Session 啟動流程

> 備註：`context/` 內的 `current-task.md`、`progress.md`、`todo.md`、`session-log.md` 是作者的本機工作筆記，未納入版控（見 `.gitignore`）；公開 repo 只有 `decisions.md` 與 `project-summary.md`（各有英文與繁體中文兩個版本：`NAME.md` / `NAME.zh-TW.md`）。

每次新的 Session，依序閱讀：

1. `AGENTS.md`（含**語言政策**：App 與所有文件都是雙語）
2. `context/project-summary.md`
3. `context/current-task.md`
4. `context/progress.md`
5. `context/todo.md`
6. `context/decisions.md`
7. `context/session-log.md`

閱讀完成後再開始工作。不得要求使用者重新解釋專案——所有長期背景都在本檔與上述檔案裡。

# 技術棧

- C# + WPF，**.NET 10 LTS**，x64（原規格 .NET 8，因 2026-11-10 EOL 改用 .NET 10，見 `context/decisions.md`）
- Win32 互通：CsWin32，需要的 API 列在 `src/DeskGrid/NativeMethods.txt`
- 不使用 Electron / WinForms / 第三方 UI 框架
- 發布：self-contained 單一 exe

常用指令：

- 建置：`dotnet build DeskGrid.slnx`
- 測試：`dotnet test DeskGrid.slnx`（xUnit v3，`tests/DeskGrid.Tests/`）
- 執行：`src/DeskGrid/bin/Debug/net10.0-windows/win-x64/DeskGrid.exe`
- 打包：`dotnet publish src/DeskGrid/DeskGrid.csproj -p:PublishProfile=win-x64` → `artifacts/publish/DeskGrid.exe`（self-contained 單一 exe，約 62 MB）
- 發布：推送 `v*` tag（例如 `v0.1.0`）→ GitHub Actions 建 Release，只附單一 exe（SHA-256 由 GitHub 在資產上顯示，更新程式讀 API 的 `digest`）
- Log：`%LOCALAPPDATA%\DeskGrid\deskgrid.log`；Config：`%APPDATA%\DeskGrid\config.json`

# 架構

每個模組一個資料夾 `src/DeskGrid/<Folder>/`，namespace `DeskGrid.<Folder>`。

| 模組 | 資料夾 | 職責 |
|---|---|---|
| DesktopHost | `Desktop/` | 把 widget 視窗掛在桌面圖示層（所有一般視窗後方）；相容舊版與 Win11 24H2+ 桌面階層；Explorer 重啟後自動重新掛載 |
| ConfigStore | `Config/` | `%APPDATA%\DeskGrid\config.json`，只存 widget 位置、面板欄數、項目排序、根目錄路徑、拖入模式，以及面板背景（2026-10-24 使用者同意新增）、刪除資料夾時項目的處理方式（`deleteMode`）、是否問過開機自啟（`autoStartAsked`，2026-10-30）、啟動時是否檢查更新（`checkForUpdatesAtStartup`，預設開，2026-10-30）、面板展開位置（`panelOpenMode`：`nearby` / `centered`，2026-11-03）、介面語言（`language`：`system` / `english` / `traditionalChinese`，2026-11-03）、是否允許腳本與 AI agent 控制（`allowControl`，預設關，2026-11-08）、鍵盤快速鍵（一般動作存在 `hotkeys`，資料夾自己的存在它的 widget 項目裡的 `hotkey`，2026-11-08） |
| FolderScanner | `Folders/` | 根目錄預設 `%USERPROFILE%\DeskGrid\`，每個子資料夾 = 一個 widget；FileSystemWatcher 監聽 Created/Deleted/Renamed/Changed，300ms debounce；Renamed 要轉移 config 中的位置與排序；根目錄位於使用者桌面時拒絕並提示 |
| IconService | `Icons/` | IShellItemImageFactory 取 256px 圖示；依排序取前 4 個合成 2×2 預覽；快取，資料夾變動才重建 |
| FolderWidget | `Widgets/` | 桌面上的資料夾圖示 + 名稱，可拖動並存位置（`FolderWidget` 視圖、`WidgetBoard` 同步與擺放、`WidgetGrid` 格線） |
| PopupPanel | `Panel/` | 格子面板（預設 3 欄，超過可捲動，不分頁）；點項目用 ShellExecute 啟動；點外部 / Esc / 失焦收合；可在 widget 旁或螢幕中央展開（`PanelOpenMode`） |
| TrayApp | `Tray/`、`Settings/` | 系統匣選單：新增資料夾、設定、開機自啟開關、結束（`TrayIcon`、`AutoStart`；設定視窗、更新紀錄視窗與新增資料夾對話框在 `Settings/`）。「開啟根目錄」「釘選所有資料夾到工作列」「GitHub 網址」「更新紀錄」在設定視窗裡（2026-10-30） |
| 拖放加入 | `Folders/ItemImporter` | 拖到 widget 的檔案依規則建捷徑 / 移動 / 複製 |
| 刪除資料夾 | `Folders/FolderDeleter`、`Shell/RecycleBin` | widget 右鍵刪除：資料夾進資源回收筒，內容依設定先移到桌面或一併刪除 |
| 疊圖示建資料夾 | `Widgets/IconMerger`、`Folders/DesktopFiles` | 桌面圖示拖到另一個圖示上 → 新資料夾（拖曳期間以一個透明接收區接手 Explorer 的放開） |
| 工作列釘選 | `Taskbar/` | widget 右鍵「釘選到工作列」：建 `--open "資料夾"` 捷徑讓使用者自己釘；設定視窗「工作列」卡片的「把所有資料夾釘選到工作列…」建 `--all` 捷徑（面板列出所有資料夾）；點下去由命名管線（`InstanceChannel`）通知執行中的 DeskGrid 在游標處開面板；捷徑各帶 AppUserModelID，工作列才不會合併 |
| 控制管道 | `Control/` | 腳本 / AI agent 操作執行中的程式：`DeskGrid.exe --ctl <指令>`（`ControlCli`）經 named pipe `DeskGrid.Control.<使用者>`（`ControlChannel`）送出一行 JSON，印出 JSON 回覆；`ControlCommands` 透過 `IControlHost`（App 的 `ControlHost`）在 UI 執行緒執行。需使用者開啟：config `allowControl`，預設關（2026-11-08） |
| 鍵盤快速鍵 | `Hotkeys/`、`Settings/` | 系統層級快速鍵：`Hotkey`（解析 / 寫出 `Ctrl+M`）、`HotkeyBindings`（哪個動作用哪組快速鍵，從 config 讀寫）、`HotkeyService`（在隱藏視窗上 `RegisterHotKey`，回報 Windows 拒絕的組合）；`Settings/` 的 `HotkeysWindow` + `HotkeyBox` 負責錄製（2026-11-08） |
| 版本檢查 | `Updates/` | 啟動後 8 秒與設定視窗的「檢查更新」按鈕：查 GitHub 最新 release，有新版本彈出「更新 / 維持 / 取消」；更新 = 下載 exe、驗 SHA256、由 PowerShell 小腳本在程式結束後換檔並重啟 |
| 液態玻璃 | `Glass/`、`Panel/PanelGlass` | 「液態玻璃」面板背景：`LiquidGlass.Render` 依螢幕畫面畫出一片圓角玻璃（模糊、邊緣折射、亮邊）；`PanelGlass` 擷取螢幕並交給面板（2026-11-09） |
| （輔助）Shell | `Shell/` | Shell 互通：`ShellImage`（圖示）、`ShellItem`（ShellExecute 開啟、顯示名稱）、`ShortcutFile`（建 .lnk）、`ShellContextMenu`（Explorer 對檔案的右鍵選單）、`RecycleBin` |
| （輔助）Log | `Diagnostics/` | 簡易 append-only log |
| （輔助）DispatcherThread | `Threading/` | 獨立 STA 執行緒上的 WPF Dispatcher（Shell / WPF 影像用；測試也拿來當 UI 執行緒） |
| （輔助）Strings | `Resources/` | `Strings.resx`（en，neutral）+ `Strings.zh-TW.resx`；build 時產生強型別 `DeskGrid.Resources.Strings`。`UiLanguage` 決定語言（設定的選擇或 Windows 語言）並整個程序切換 |

## DesktopHost 實作要點（階段 1 實測）

- **掛載點**：widget 是 `HwndSource`（`WS_CHILD`，`UsesPerPixelTransparency` → `WS_EX_LAYERED`），父視窗為 `SHELLDLL_DefView`，z-order 在 `SysListView32`（圖示）之上 → 可接收點擊、在所有一般視窗後方、Win+D 後仍可見。
- **兩種階層**（`IconLayer.Find()` 都處理，先送 Progman `0x052C`）：
  - 新版（24H2+，本機 build 26300 實測）：`Progman`[`WS_EX_NOREDIRECTIONBITMAP`] → `SHELLDLL_DefView`[`WS_EX_LAYERED`] → `SysListView32`；另有 `Progman` → `WorkerW`（子視窗）。
  - 舊版（Win10 ~ Win11 23H2）：top-level `WorkerW` → `SHELLDLL_DefView`（split 前在 `Progman` 下）。**尚未實機測試。**
- **必須 layered**：24H2+ 的 Progman 是 composition-based，非 layered 子視窗不會被畫出；layered 也讓全透明像素可點穿。
- **Explorer 重啟**：explorer 結束時跨行程子視窗會被一併銷毀 → `DesktopSurface.Lost` → `DesktopHost` 每秒重試，以新 `HwndSource` 掛到新的 DefView，沿用同一份 WPF content（狀態保留）。`TaskbarCreated` 廣播為第二觸發（需由隱藏的 top-level 視窗接收，message-only 視窗收不到廣播）。
- **座標**：surface 位置為實體螢幕像素（`ScreenPoint`），掛載時轉 DefView client 座標。
- **多螢幕**（2026-11-01，使用者回報「接外接螢幕、放在主螢幕左邊後 widget 全跑到左邊，且在左邊開的面板卻出現在主螢幕」）：桌面視窗（Progman / DefView）涵蓋所有螢幕，在主螢幕**左側或上方**加螢幕時它的原點會往負方向移，子視窗（surface）的 client 座標不變 → 在螢幕上整批漂移；`DesktopSurface.Position`（設定檔存的、面板定位用的）沒變，所以面板開在「邏輯位置」＝主螢幕。修法：`DesktopSurface.Resync()`（`GetWindowRect` 與 `Position` 不同就 `MoveTo`）由 `DesktopHost` 每秒檢查 + 收到 `WM_DISPLAYCHANGE`（隱藏的 top-level 視窗）立即執行；1.5 秒後（Explorer 重排完）發 `DisplayChanged` → `WidgetBoard.OnDisplayChanged`（重讀格線、依**設定檔存的位置**重新擺、關掉開著的面板）。存檔位置的中心不在任何螢幕工作區（螢幕被拔掉）→ 暫放最近螢幕最近空格，**不覆蓋存檔**，螢幕回來就回去（`WidgetBoard.Add` 與 `OnDisplayChanged`；拖曳才會存新位置）。實機重現並驗證：用 `ChangeDisplaySettingsEx`（`DM_POSITION`，`CDS_UPDATEREGISTRY | CDS_NORESET` 再用 NULL 套用；套用時 dm 必須傳 NULL）把第二螢幕在主螢幕右側（x=2880）與左側（x=-2360）間切換，Progman 矩形從 (0,0,5240,1800) 變 (-2360,0,2880,1800)，log 出現「Put 9 surface(s) back」，9 個 surface 螢幕座標不變；測完還原。
- **版面設定變更**（2026-11-03，issue #21）：隱藏的 top-level 視窗收到 `WM_SETTINGCHANGE`，`wParam` 是 `SPI_SETWORKAREA`（工作列移動 / 改大小）、`SPI_ICONHORIZONTALSPACING` / `SPI_ICONVERTICALSPACING` / `SPI_SETICONMETRICS`（桌面圖示間距）、`SPI_SETLOGICALDPIOVERRIDE`（縮放）之一時（`DesktopHost.ChangesDesktopLayout`），與 `WM_DISPLAYCHANGE` 共用同一個 1.5 秒 `DisplayChanged`。`WidgetBoard.OnDisplayChanged` 現在對螢幕上的 widget 也會吸到**今天**格線上最近的空格並存檔（格線跟著桌面圖示 `FollowsIcons` 時才吸，讀不到圖示時維持原位）。沒有實機改工作列 / 間距驗證。
- `WS_EX_NOPARENTNOTIFY`：避免 Explorer 執行緒卡住時透過 parent notify 卡住我們。
- **軟體繪圖**（2026-11-03，輕量化）：widget 的 `DesktopSurface` 與隱藏的 `ShellMessageWindow` 設 `CompositionTarget.RenderMode = SoftwareOnly`，沒開過面板前不建 Direct3D 裝置（實測 private 117 → 83 MB，widget 截圖與硬體繪圖只差邊緣抗鋸齒 ≤ 15/255）。面板維持硬體繪圖：軟體繪圖下 DWM 延伸框架（Acrylic）可能變黑；整個程序設 `ProcessRenderMode` 不可用。開過面板後 D3D 裝置會建立（約 +60 MB，之後會回落）。

## Config / Folders 實作要點（階段 2）

- **資料來源**：資料夾內容只來自磁碟；config 只補檔案系統存不了的東西。`WidgetConfig` 以資料夾名稱為 key（不分大小寫）：`Position`（實體螢幕像素，未擺放前為 null）、`ItemOrder`（檔名清單）。`PanelColumns`（全域，預設 3）、`DropMode`（move/copy）、`RootPath` 為全域。
- **項目**：widget 資料夾內的可見檔案（略過隱藏/系統檔，如 desktop.ini）；子資料夾不算項目；根目錄下的散檔忽略。首次依 Explorer 自然排序（`StrCmpLogicalW`），新項目接在最後，消失的項目從排序移除。
- **執行緒**：`FolderScanner` 必須在有 `SynchronizationContext` 的執行緒（UI）建立；所有事件依發生順序 post 回該 context，因此 rename 一定先於隨後的 refresh 被處理。整個 config 只在 UI 執行緒存取，不需鎖。
- **Rename**：`WidgetRenamed` / `ItemRenamed` 立即轉移設定並存檔；其他變動 300ms debounce 後整個重掃（資料量小，重掃比追蹤差異簡單）。watcher 溢位 → 重掃。
- **根目錄被刪又重建**（2026-11-03，issue #24）：`FileSystemWatcher` 會隨被監看的資料夾一起死。`FolderScanner` 在 watcher 發 `Error`、或安靜期後發現根目錄不存在時，每秒檢查一次根目錄（`_recoveryTimer`），回來後在 UI context 重建 watcher 並發 `Changed`；根目錄不存在期間重掃回 `null`，所以設定不會被清掉。
- **捷徑目標被更新**（2026-11-03，issue #25）：`ShortcutTargetWatcher` 每個放有捷徑目標的資料夾（非遞迴，略過網路路徑）一個 watcher，只在目標檔名被寫入 / 建立 / 改名時觸發 `FolderScanner` 的安靜期計時，重掃後 `TargetWriteTimeUtc` 變了、圖示快取 key 隨之改變而重畫。每次 `Scan` 之後同步 watcher 清單。
- **清除**：重掃時移除已不存在資料夾/項目的設定。根目錄讀不到（例如磁碟消失）時**不**重算，避免一次清光所有設定。
- **寫檔**：先寫 `.tmp` 再 rename；壞掉的 config 改名為 `config.json.<時間>.bad` 後用預設值。
- **根目錄驗證**（`RootFolder.Prepare`）：須為完整路徑、不可在使用者桌面或公用桌面內、可建立。啟動時不合格 → 提示後改用預設並寫回 config。
- **之後的階段**：位置載入時若跑出工作區要夾回（階段 4）。

## Icons 實作要點（階段 3）

- **一律圖示，不用內容縮圖**：`SIIGBF_ICONONLY`，捷徑取目標圖示、不含箭頭 overlay。實測 256px 時 Shell 永遠回傳 256×256（`SCALEUP` 無差別）。
- **Alpha**：Shell 回傳的 DIB section 是**直通（非預乘）alpha**（實測許多像素 RGB > A），以 `Bgra32` 建 `BitmapSource`；`CreateBitmapSourceFromHBitmap` 會丟掉 alpha，不可用。bottom-up DIB 要翻列。
- **執行緒**：Shell 呼叫與預覽合成都在專用 STA 執行緒（`DispatcherThread "DeskGrid.Shell"`）；結果皆為 frozen bitmap，可跨執行緒。`IconService` 本身只在 UI 執行緒呼叫（快取是一般 Dictionary）。
- **快取**：僅記憶體。圖示 key = (路徑, LastWriteTimeUtc, 捷徑目標的 LastWriteTimeUtc)（`FolderItem.TargetWriteTimeUtc`，`FolderScanner` 掃描時用 `ShortcutFile.TryGetTarget` 讀 .lnk 目標；程式更新換圖示時捷徑本身沒變，只看 .lnk 會一直顯示舊圖示；目標改變要等下次重掃才會重畫），快取的是 `Task`，同時重複請求只取一次。預覽 key = 前 4 個項目的 (路徑, ticks)，第 5 個以後變動不重建。`Retain(widgets)` 在每次 catalog 刷新時清掉已消失/已變更的項目。
- **預覽版面**（`PreviewComposer`）：256×256 透明圖，2×2、間距 8%、依列填滿；不足 4 個留空格。資料夾底色由 widget 自己畫（階段 4）。

## Widgets 實作要點（階段 4）

- **外觀**（使用者選定）：48 DIP 淺色半透明圓角方塊（白 30%、圓角 22.37%）內放預覽，下方名稱用系統圖示字型、白字陰影、最多 2 行；滑鼠移上時整格淡白高亮（像原生圖示）。
- **格子**：一格 = 桌面原生圖示格（`DesktopIconGrid` 對 Explorer 的 `SysListView32` 送 `LVM_GETITEMSPACING` / `LVM_GETITEMPOSITION`，位置要跨行程讀取，所以用 `VirtualAllocEx` + `ReadProcessMemory`）；widget 一格就是一個原生圖示格大小（不取整數倍，否則高 DPI 下 76×96 DIP 會比 150×182 px 的圖示格大一點而變兩倍大）。格線以第一個原生圖示的格為錨點，所以與桌面圖示對齊；每欄由上往下填、從右往左。讀不到時退回舊行為（工作區右上角起算、76×96 DIP）。每個 widget 都在格子上，且一格一個。
- **避開原生圖示**：與原生圖示格重疊超過 1/4 的格不使用（`WidgetGrid` 的 obstacles / `Covers`），除非已無其他格。擺放 / 放開拖曳 widget 時由 widget 避開。挑選順序（`WidgetGrid.Candidates`，2026-11-06 修正）：沒有 widget 也沒有圖示的格 → 沒有 widget 的格（圖示底下）→ 任何格；只要還有沒有 widget 的格，就不會和別的 widget 共用一格。
- **圖示讓開**（使用者選定，2026-10-28）：之後若有原生圖示落到 widget 格上（Explorer 把新捷徑放進它以為的空格、使用者把圖示拖過去），`WidgetBoard.ClearIconsOffWidgets` 把該圖示移到最近的空格（`LVM_SETITEMPOSITION`，座標放在 lParam，不必寫入 Explorer 記憶體）。桌面開了「自動排列圖示」（list view 有 `LVS_AUTOARRANGE`）時圖示移不動，改由 widget 讓開。
- **偵測圖示變動**（`DesktopIconWatcher`）：`SetWinEventHook` 只掛桌面那條 Explorer 執行緒（與工作列不同），out-of-context、不輪詢。list view 的 `EVENT_OBJECT_CREATE/DESTROY/REORDER` = 新增 / 刪除；**移動圖示時 list view 不發任何事件**（實測，含 `LVM_SETITEMPOSITION`），改用該執行緒的 `EVENT_SYSTEM_CAPTUREEND`（每次桌面拖曳結束都有）。300 ms debounce 後重讀格子。游標停在 widget 上放開 = widget 的拖放（加進資料夾），不會變成重疊。
- **擺放**：有存位置 → 吸到最近的空格；沒存過 → 主螢幕第一個空格（先處理有存位置的，新 widget 才不會佔掉它們的格）。結果寫回 config（沒變就不寫）。
- **框選與一起拖動**（2026-11-03，issue #26，使用者選定「框選時一併選取、可一起拖動」）：Explorer 自己畫框選框、選自己的圖示，`MarqueeSelector` 只讀滑鼠：`DesktopPressed`（`EVENT_SYSTEM_CAPTURESTART`）時若左鍵按著且游標不在任何桌面圖示格上，就每 20 ms 看游標，超過拖曳距離後以按下點 → 游標（`ScreenRect.Between`）為框，`WidgetBoard.SelectWithin` 選取圖片 + 名稱區（格子內縮左右 15% / 上下 10%，`WidgetGrid.IconArea`）與框相交的 widget（`FolderWidget.IsSelected`，淡白底）。任何桌面按下或點 widget 都清除選取；拖曳已選取的 widget → `Entry._group` 是整個選取，一起移動、一起 Esc 取消、放開時逐一吸附存檔（資源回收筒刪除只在單一 widget 時）；拖曳沒被選取的 widget 先清除選取。Ctrl+A / Ctrl 加選沒做。**完全沒有實機驗證**（無法操作桌面框選）。
- **「顯示桌面圖示」關閉**（2026-11-03，issue #23）：實測（explorer 啟動的探測腳本送 `WM_COMMAND 0x7402` 給 DefView）：Explorer 只隱藏 `SysListView32`，DefView 與我們的 surface 不受影響，所以 widget 本來會留著。現在 `DesktopHost` 每秒檢查 list view 的 `IsWindowVisible`，變了就對所有 surface `SetVisible`（`ShowWindow` `SW_HIDE` / `SW_SHOWNA`），新掛上的 surface 也套用目前狀態。
- **拖曳中 Esc 取消**（2026-11-03，issue #22）：widget 不啟用就收不到鍵盤事件，所以拖曳期間 `FolderWidget` 以 30 ms `DispatcherTimer` 看 `GetAsyncKeyState(VK_ESCAPE)`，按下就放掉滑鼠捕捉、發 `DragCancelled`（之後的放開滑鼠被忽略，不觸發點擊或 `DragCompleted`），`WidgetBoard` 把 widget 搬回 `_dragOrigin`（原本就是存檔的格，不必吸附 / 存檔）。
- **拖曳**：`FolderWidget` 只把滑鼠轉成 Clicked / DragStarted / Dragged(游標位移, 實體像素) / DragCompleted，超過系統 `SM_CXDRAG` 才算拖。拖曳中自由跟隨游標；放開後吸到最近空格（目標格有人 → 次近空格）並存檔。
- **生命週期**：`WidgetBoard.Show` 依資料夾名稱比對：消失的移除、新的建立、既有的更新名稱與預覽。資料夾改名 = 舊 widget 移除、新 widget 在轉移過來的位置建立。
- **DesktopHost**：`Add` 在已有圖示層時立即掛上（快取上次找到的 DefView，`IsWindow` 檢查），`Remove` 銷毀視窗；建立子視窗失敗（`Win32Exception`）→ 進入重試。
- **右鍵選單**：「釘選到工作列…」「新增資料夾…」「刪除資料夾…」。
- **不搶啟用**：`DesktopSurface` 對 `WM_MOUSEACTIVATE` 回 `MA_NOACTIVATE`。否則點 widget 會啟用 Explorer 桌面，而 Explorer 會吞掉這次啟用點擊（面板開著時 widget 收不到按下）。

## Panel 實作要點（階段 5）

- **外觀**（使用者選定）：開在 widget 旁（下 → 上 → 左 → 右，先放得下者，另一軸夾回工作區）；項目 48 DIP 圖示、76×96 DIP 格；上方顯示資料夾名稱；最多顯示 4 列，超過捲動（細捲軸）。
- **展開位置**（2026-11-03，config `panelOpenMode`，設定「資料夾展開方式」）：兩種模式。**附近展開**（`PanelOpenMode.Nearby`，預設）即上述行為：開在 widget 旁，大小依項目數（最多 4 列）。**置中展開**（`PanelOpenMode.Centered`）讓每個資料夾的面板都開在 widget 所在螢幕工作區的正中央（`PanelPlacement.Center`），且無論資料夾裡有多少項目都是**同樣大小**，不同資料夾的位置與大小不會不一致：寬 `columns` 欄、高 `columns` 列（正方形；只有螢幕工作區太矮時才減列，`PopupPanel.FixItemsSize`），項目在裡面捲動（捲軸的位置永遠預留，出現時不會把面板撐寬；空資料夾的提示與項目共用同一格，所以空資料夾一樣高；`MakeRoomForIncoming` 不調整大小）。所有資料夾列表也是同樣大小。拖曳停在 widget 上而開啟的面板（`OpenForDrag`，不啟用）**一律附近展開**，不管設定：游標沒辦法橫越整個螢幕到置中的面板而不被當成離開。設定在面板開啟時讀取（`PanelController.Open`）。沒有實機驗證（單元測試驗證大小與位置的計算）。
- **背景可自訂**（config `panelBackground`）：`system`（預設，Win11 22H2+ 系統 Acrylic + 系統深淺色；其他版本為不透明系統色）、`dark`、`light`（半透明）、`custom`（`customColor` #RRGGBB + `customOpacity` 0–1，字色依背景亮度自動黑/白）。設定介面在階段 6。**其他樣式的不透明度**（2026-11-09，config `panelBackground.opacity`，`null` = 該樣式自己的預設，不寫進檔案）：設定的滑桿現在每種樣式都能用（`PanelTheme.DefaultOpacity`：深色 82 %、淺色 80 %、自訂用 `customOpacity`、跟隨系統 85 %，在還是 Acrylic 時顯示這個值）。`dark` / `light` 把它當成顏色的 alpha；**`system` 一旦設了不透明度就不再是 Acrylic**：DWM 的毛玻璃無法調透明度，所以改用系統自己的表面色（深色 / 淺色那個）配上該 alpha 繪製（滑桿下有一行提示說明）。與樣式預設值相差不到半格的值（滑桿吸附 5 %，82 % 顯示為 80 %）存成 `null`，所以除非使用者真的動了滑桿，跟隨系統仍是 Acrylic。切到別的樣式時，滑桿若沒被使用者動過，就改成那個樣式自己的不透明度。自訂仍用自己的 `customOpacity`。尚未在真實桌面上看過（測試檢查顏色與視窗回報的內容）。
- **液態玻璃背景**（2026-11-09，`panelBackground.style` = `liquidGlass`；使用者要求做 iOS 風格的玻璃，並要先找開源專案、依其授權使用）：面板自己是一個視窗，不連面板一起讀就無法即時讀取它後面的畫面。所以 `PanelGlass` 用 `ScreenSampler.Capture`（BitBlt）把該螢幕的工作區讀一次（第一次 `Reposition`，在 `SourceInitialized`，**視窗顯示之前**，面板才不會在畫面裡），再由 `LiquidGlass.Render`（CPU、C#）依視窗的範圍畫出面板背景：圓角矩形距離場（圓角 20 DIP）、模糊過的畫面（sigma 3 DIP，三次 box blur）、沿邊緣法線往內移動一個依 OverShifted/LiquidGlass 的衰減曲線決定的距離再取色（紅與藍略有差異：邊緣色散）、輕微提高飽和度與亮度、依「不透明度」設定混入表面色（預設 25 %，依系統深淺色選深或淺）、OverShifted 的方向性邊緣光暈，以及 1.5 px 的亮邊。結果是預乘 alpha 的點陣圖，當成 Frame 的 `ImageBrush`；視窗和其他非 Acrylic 樣式一樣是分層視窗（`AllowsTransparency`）。第一次繪製立即進行；之後的（拖入東西時面板長大、DPI 改變）在最後一次 `Reposition` 之後等 40 毫秒，期間舊圖被拉伸。畫面只在面板開著時保留。**文字顏色跟著玻璃**：依第一次繪製的平均亮度（大於 0.5 用深色字，否則白字；`PanelTheme.WithTextFor`），不管系統主題是什麼，整個面板生命週期不再變。讀不到螢幕時面板是 85 % 的純表面色。不是即時畫面：開著的面板後面有東西在動不會反映。**授權**：兩個來源都是 MIT；聲明與授權條文在 README（「第三方聲明」，設定與 Release 說明也有連結），著作權行放在 `Glass/LiquidGlass.cs` 開頭。看過但**沒有採用** WPF demo `dragosniamtu/WPF-Liquid-Glass-Effect`：它的 shader 改編自一篇沒有程式碼授權的 Medium 文章（它自己的聲明說明要取得許可才能散布），而且依賴非 MIT 授權的 NuGet 二進位檔。已在真實桌面上看過（用 explorer 啟動的 dev build，面板在桌布上的截圖）；沒看過：其他 DPI / 第二個螢幕、面板在明亮視窗上的樣子。
- **Acrylic 做法**（實測）：無邊框 `WS_POPUP` 視窗 DWM **不畫** system backdrop，必須保留框架：`SingleBorderWindow` + `WindowChrome(GlassFrameThickness=-1, CaptionHeight=0)` + 拿掉 `WS_SYSMENU`（否則 DWM 在延伸框架畫關閉鈕）+ composition 背景透明 + `DWMWA_SYSTEMBACKDROP_TYPE=TRANSIENTWINDOW`、`DWMWA_WINDOW_CORNER_PREFERENCE=ROUND`。**不可用 `SizeToContent`**（WPF 會多加已被 WindowChrome 移除的外框尺寸）→ 自己 Measure 內容後設 Width/Height。非 Acrylic 樣式用 layered 視窗（`AllowsTransparency`）自己畫圓角底色。
- **固定在開啟的那一側**（2026-10-31）：`PanelPlacement.Side` 只在第一次定位（及換顯示別的資料夾 / 列表）時選；之後變大（拖入時 `MakeRoomForIncoming` 長一列、放入後重掃）只在同一側夾回工作區（可能蓋住 widget 一部分）。否則 widget 在螢幕下方時，長高後放不下 → 跳到右側 → 游標不在面板內 → DragLeave 收回 → 又放得下回到下方 → 無限來回 = 嚴重閃爍（使用者實測，log 證實）。
- **多螢幕**（2026-11-01）：面板在 widget 中心所在螢幕的工作區定位，位置與大小一律用**該螢幕**的 DPI 換算（`Reposition(window, size)` 一律帶大小）。視窗先建在別的 DPI 的螢幕再被 `SetWindowPos` 搬走時，Windows 會送 `WM_DPICHANGED` 並建議一個由舊尺寸縮放的位置與大小，WPF 照單全收 → `OnWindowMessage` 收到後以 `_size`（要的 DIP 大小；不能信 `Width`/`Height`，WPF 已被改過）重新 `Reposition`。
- **定位**：在 `SourceInitialized`（第一次顯示前）用實體像素 `SetWindowPos`，不會閃到別處；內容變動（即時刷新）後重新定位。改大小一律先 `SetWindowPos` 新位置 + 新大小、再設 WPF 尺寸（`Resize`）：先長再移的話，貼螢幕底部時超出螢幕那段不會重畫，最後一列名稱被切掉。
- **收合**：Esc、失去啟用（點外部）、點項目啟動後、再點同一個 widget；點另一個 widget 直接切換。**截圖例外**（`ScreenCapture`）：失焦時前景若是 Windows 截圖工具（`SnippingTool`、Win10 `ScreenClippingHost`）不收合，掛 `EVENT_SYSTEM_FOREGROUND` 等前景離開截圖工具，回到面板照常、到別處才收合。同時只有一個面板。資料夾內容變動時面板即時更新，資料夾消失則關閉。
- **啟動**：`Process.Start(UseShellExecute=true)`（即 ShellExecuteEx），工作目錄 = 項目所在資料夾（捷徑自己的工作目錄優先），`ErrorDialog=true` 讓 Windows 顯示自己的錯誤（例如捷徑目標不存在）；UAC 取消等例外只記 log。
- **名稱**：項目名稱用 Shell 顯示名稱（`SIGDN_NORMALDISPLAY`，與檔案總管一致，`.lnk`/`.url` 不顯示副檔名、其他依使用者「隱藏副檔名」設定）。
- Button 的佈景主題樣式會強制黑字，項目樣式要明確綁回視窗 `Foreground`。
- **面板內拖曳**：項目拖超過系統拖曳距離即以 OLE `DoDragDrop`（`FileDrop` + 自訂格式 `DeskGrid.PanelItem` / `DeskGrid.PanelFolder`）開始。放回同一個面板 → 重排（`WidgetCatalog.MoveItem` 改 `ItemOrder`）；放到桌面 / 資料夾由對方處理（Explorer 移動檔案）；放到別的 widget → `ItemImporter` 對 root 底下的檔案一律「移動」。拖曳中面板失焦不收合，結束後若已離開才收。
- **拖出偵測**：游標是否在面板內一律用 `GetWindowRect` 實體像素比（DIP 換算會讓剛離開右 / 下邊的游標被判在內）。`PopupPanel` 的 `_watch`（50 ms DispatcherTimer，`OnWatch`）看游標，不只靠 `DragLeave`：游標離開面板**與其 widget**（`_anchor`）超過 300 ms 才算拖出（`TakeOut`），從面板移到 widget 中間的空隙不會關。拖曳中的重掃（`PopupPanel.Refresh`）延到放開後；影子落地前該項目的格即使被重建也保持隱藏（`_settlingItem`）。
- **所有資料夾列表**（`ShowList`，工作列 `--all`）：格子是資料夾（底板 + 2×2 預覽，`PanelItem.Folder`）；點一下在同一視窗 `ShowFolder`，標題左側 ← 回列表；列表模式可拖動排序（0.2.1 起）、不可改名；**接受檔案放到資料夾格上**（2026-11-03，issue #11：`FolderUnder` 找出游標下的格，`TintDropTarget` 用按鈕的 `Tag`〔範本的 Border 背景綁它〕上色，放開 → `ItemsDropped(資料夾名, 路徑, 該資料夾現有項目數)`，與放到 widget 同一套匯入規則）。
- **重新量測**：換內容後量面板大小要先 `UpdateLayout()`（`MeasureContent`）——換 `ItemsSource` 只標記 WrapPanel，直接 `Frame.Measure` 會拿到舊尺寸。
- **工作列開面板的位置**（2026-11-03，issue #13 / #12）：第二個程序在 `OnStartup` 第一件事讀游標（`OpenMessage`：`x TAB y TAB 名稱` 一行寫進命名管線），面板開在**點擊當下**的位置，不是請求抵達時（約 1 秒後）的游標；第一個程序被 `--open` 啟動時同樣在啟動時讀。釘的資料夾已被刪掉 → 開「所有資料夾」列表（以前只記 log）。
- **工作列面板緊貼工作列**（2026-11-03，issue #27）：錨點是游標（在工作列上、工作區外），以前面板底邊 = 游標 Y − 間距，浮在工作列上方多遠看游標在按鈕的哪裡（「偶發」）。`PanelPlacement.Within` 先把錨點夾進工作區再算，面板固定離工作區邊緣「1 px + 間距」。**沒有看到 issue 的截圖**，這是依描述與程式推出的原因，待使用者確認。
- **從開始功能表拖 App**（2026-11-03，issue #19）：App 沒有檔案路徑，拖曳資料只有 `Shell IDList Array`（CIDA）而沒有 `FileDrop`。`DroppedItems.Read` 在沒有 `FileDrop` 時用 `ShellIdList.Parse` 取出絕對 PIDL，對不是檔案 / 資料夾的項目（`ShellItem.Describe`：`SIGDN_FILESYSPATH` 取不到）用 `ShortcutFile.CreateFromIdList`（`IShellLink.SetIDList`）在 `%TEMP%\DeskGrid\drops\<guid>\` 做暫存 `.lnk`（以顯示名稱命名），之後與一般拖入的捷徑相同（複製進資料夾），`Dispose` 刪掉暫存。三個放開處（`WidgetBoard.ImportDropped`、`PopupPanel` 的資料夾面板與列表）都改用它；游標效果顯示連結（來源不給就複製）。測試用「本機」這個虛擬項目走完整條路；**沒有真的從開始功能表拖過**，所以 Start 實際給的資料格式（是否真有 `Shell IDList Array`、PIDL 是否為 AppsFolder 項目）未驗證。
- **工作列按鈕再按一下 = 收合**：點工作列會先讓面板失焦關閉，之後同一次點擊的請求才從管線到達。關閉時若前景是 `Shell_TrayWnd`，`PanelController` 記下是哪個面板，3 秒內同一面板的工作列請求視為「收合」而不重開。
- **拖曳動畫**（iOS 式）：`DragGhost` 是不接輸入的置頂分層視窗（純 Win32 視窗 + `UpdateLayeredWindow`，**不可用 `HwndSource`**：WPF 也會畫進去，ULW 失敗 → 黑色方塊），載著圖示跟游標（每次 `QueryContinueDrag` 更新位置、被新開的面板蓋住時重新置頂）；原格留空（容器 `Opacity=0`），其餘項目用 `TranslateTransform` 動畫滑到新格（`PopupPanel.Slide`）。拖離面板時缺口合起來；放下後影子飛到格子，被別處接走則縮小淡出。拖曳事件的 `DragLeave` 會從子元素冒上來，要以游標是否真的離開視窗判斷。
- **拖到 widget 自動開面板**：`WidgetBoard` 的 widget 上拖曳停 550 ms（且該拖曳可放入，或是從**該資料夾**面板拖出的項目 `PanelDragData.IsFrom`）→ `DragHovered` → `PanelController.OpenForDrag`（不啟用）。原面板只 `HideForDrag`（DWM cloak），拖曳結束才關，因為 `DoDragDrop` 在它裡面跑（controller 記在 `_dragSource`）。拖回原資料夾的 widget → 原面板 `ShowAgainForDrag`（取消 cloak、原格留空）可放回。放進開著的面板：`ItemsDropped` → `ItemImporter.ImportTo` → `WidgetCatalog.PlaceNewItem`（先重掃才能指定位置）；放入前該面板先開缺口。widget 的 DragLeave 也會從子元素冒上來，用 `FolderWidget.IsCursorOver()` 判斷是否真的離開，計時才不會一直重來。**必須用 `WindowFromPoint` == widget 自己的 HWND**，不能只比矩形：開著的面板會蓋住下方的 widget，只比矩形時被蓋住的 widget 以為游標還在、計時到了就把面板換成它的，兩個面板來回切換 = 嚴重閃爍（2026-10-31 使用者回報）。
- **拖曳打開的面板自動收合**（2026-10-31，`OnWatch` 的 `_followsDrag`）：拖曳中游標離開面板與 widget 超過 300 ms → 關；放開滑鼠（左或右鍵）250 ms 後仍沒有東西放進來（`TookDrop`）→ 關；有放進來 → `Activate()`，Windows 不給焦點（來源是別的 App，例如 Explorer）時改等「在面板與 widget 以外按下滑鼠」才關（此時 Esc 無效）。拖曳來源是我們自己的面板時，`DragFinished` 直接決定：有放進來就啟用，否則關閉。
- **widget 整格可點、可放**：`Highlight` 平時是 alpha=1 的底色（看不見但會被命中），否則格內透明處的放開會落到 Explorer 桌面、被當成「圖示落在 widget 上」再推開。拖曳 widget 時 `DesktopSurface.BringToFront()`，從其他 widget 上方經過。
- **面板內右鍵**（2026-10-30）：檔案項目 → `ShellContextMenu`（`IShellItem.BindToHandler(BHID_SFUIObject)` 取 `IContextMenu`，`QueryContextMenu` 填原生選單，`TrackPopupMenuEx` 顯示，`InvokeCommand` 帶 `CMINVOKECOMMANDINFOEX` 執行）。是**傳統**選單（Win11「顯示其他選項」那個），沒有 Win11 精簡選單的公開 API。（2026-11-03，issue #14：曾用 WPF 仿製精簡選單，後依使用者要求移除，因為要的是 Explorer 自己的原生選單，不是仿製品；不要再做仿製。）不傳 `CMF_CANRENAME`（重新命名要 Explorer 的 view 才能編輯，這裡沒有），按住 Shift 才傳 `CMF_EXTENDEDVERBS`。「傳送到」「開啟方式」等子選單與自繪項目靠 `IContextMenu2/3` 的 `HandleMenuMsg(2)`，由 `HwndSource.AddHook` 轉送 `WM_INITMENUPOPUP` / `WM_DRAWITEM` / `WM_MEASUREITEM` / `WM_MENUCHAR`。選單與指令執行期間 `_menuOpen` 為 true，`Deactivated` 不收合（刪除確認、內容視窗會搶走啟用，面板要活到指令結束）；結束後若面板已不是前景就收合。`ShellContextMenu` 不 `ReleaseComObject` 選單物件（交給 finalizer），因為「內容」「開啟方式」可能在 `InvokeCommand` 返回後還在別的執行緒用它。資料夾格（所有資料夾列表）與名稱列右鍵 → 自己的 WPF 選單（新增資料夾… / 刪除資料夾…），按下後先收合面板再觸發事件（兩者都會問問題）。
- **方向鍵導覽**（2026-11-03，issue #20）：`PopupPanel.OnKeyDown` 用 `PanelNavigation.NextSlot`（純函式，有單元測試）決定方向鍵 / Home / End 的目標格（到邊界停住、不環繞；沒有選取時 ↓→Home 到第一格、↑←End 到最後一格；最後一列較短時 ↓ 到最後一格），`Focus()` + `BringIntoView()` 該格的 `Button`。Enter / 空白鍵由 Button 本身觸發點擊。標題輸入框有焦點時不攔截。
- **拿不到焦點的拖曳面板用 Esc 關閉**（2026-11-03，issue #9）：`OnWatch` 的 `_awaitsClick` 階段（放進東西後 Windows 不給焦點）除了「在面板與 widget 以外按下滑鼠」，也看 `GetAsyncKeyState(VK_ESCAPE)`。拖曳進行中的 Esc 由 OLE 拖曳來源（Explorer）自己處理成取消拖曳。沒有實機從 Explorer 拖放驗證。
- **標題改名**：面板標題滑過顯示鉛筆（Segoe MDL2 `E70F`），點擊變輸入框；Enter / 失焦確認、Esc 取消。`App.RenameFolder` 驗證（`WidgetFolderName.Validate`）後 `Directory.Move`；只差大小寫時經暫存名稱（`Directory.Move` 拒絕同名）；成功後面板先把自己的 `Folder` 改成新名，免得緊接的重掃找不到而關閉。

## 拖放 / Tray / 設定 實作要點（階段 6）

- **拖放規則**（`ItemImporter.Plan`，使用者選定）：`.exe` → 建 `.lnk`（絕不移動或複製 exe 本體）；**直接放在**使用者或公用桌面上的 `.lnk`/`.url` → 依 `DropMode`（預設移動）；其他地方的 `.lnk`/`.url` → 一律複製；一般檔案與資料夾 → 建 `.lnk`；已在該資料夾內、或拖的是該資料夾本身/上層 → 忽略。同名自動加「 (2)」。公用桌面捷徑沒權限移動 → 改複製。
- **捷徑命名**：exe 用檔名（`notepad.exe` → `notepad.lnk`）、其他保留完整檔名（`report.pdf.lnk`）、資料夾用資料夾名、磁碟機根目錄用路徑去掉檔名不能有的字元後剩下的部分（`D:\` → `D.lnk`；以前會變成 `D:\.lnk` 這個路徑，捷徑落在磁碟機根目錄，2026-11-06 修正）；工作目錄 = 目標所在資料夾。
- **拖放接線**：`FolderWidget` 只負責 `AllowDrop` 與拖曳中的高亮；`WidgetBoard` 處理 `DragOver`（游標顯示第一個可處理項目的效果：移動 / 複製 / 連結）與 `Drop`。檔案操作自己做完後對來源回報 **Copy，絕不回報 Move**，否則來源會以為要刪掉原檔。OLE 拖放可送達 Explorer 底下的跨行程子視窗（已用 OLE 拖曳實測）。
- **原生選單深色模式**（2026-11-03，issue #18）：`Shell/MenuTheme` 以 ordinal 135 / 136 呼叫 uxtheme 的 `SetPreferredAppMode(AllowDark)` / `FlushMenuThemes`（未公開 API，經 CsWin32 的 `LoadLibrary` + `GetProcAddress` 取得，Windows 10 1903 以前不做），系統匣選單與面板項目右鍵選單每次顯示前呼叫；缺函式就維持淺色。測試 `MenuThemeTests` 在這台 Win11 確認 ordinal 存在；**沒有目視確認選單真的變深色**。設定視窗本來就跟隨系統深色（Fluent）。
- **系統匣**（`TrayIcon`）：`Shell_NotifyIcon` + `NOTIFYICON_VERSION_4`，左右鍵都開原生 Win32 選單（每次重建，勾選狀態即時）；選單動作在選單關閉後才執行（可能開模態視窗）；`TaskbarCreated` 時重新加入圖示。圖示用 `LoadIconMetric` 從 exe 的 `#32512`（`<ApplicationIcon>`）載入 → **manifest 必須宣告 Common Controls v6**。
- **開機自啟**（`AutoStart`）：`HKCU\...\Run\DeskGrid = "exe 路徑" --autostart`；工作管理員「啟動應用程式」停用時會在 `StartupApproved\Run` 留旗標（第一個 byte 為奇數）→ 視為關閉；我們切換時會清掉該旗標。啟動時若 exe 被搬走或舊項目沒有 `--autostart` 就更新。
  - **開機流程**（2026-10-30）：帶 `--autostart` 的啟動會先 `DesktopHost.WaitUntilSettledAsync`（`DesktopReadiness`，每 0.5 秒在背景執行緒讀桌面圖示位置，連兩次相同才算好；逾時 45 秒照常往下）再建 widget。使用者自己啟動時桌面一定好了，不等。原因：登入時 list view 已存在但圖示還沒排好，格線沒有錨點 → widget 被吸到退回用的格線並寫回 config，覆蓋真正的位置。`WidgetBoard.Add` 另有防線：讀不到圖示格線（`WidgetGrid.FollowsIcons` 為 false）時，有存位置的 widget 不吸附、不存檔。
  - **第一次執行**：config `autoStartAsked` 為 false 時，widget 出現後問一次「要不要開機啟動」（已啟用的使用者靜默標記）；之後只能從系統匣選單切換。登錄寫入被拒（群組原則）時 `ReportAutoStart` 會跳警告。
- **單一執行個體**：`Local\DeskGrid.SingleInstance` mutex，第二個直接結束。
- **設定視窗**：語言（*跟隨 Windows* / *English* / *繁體中文*，config `language`；按「確定」後整個程序立即套用，`UiLanguage.Apply`，之後開啟的視窗與選單都用新語言）、根目錄（`OpenFolderDialog`；旁邊「開啟」用框裡的路徑在 Explorer 開，資料夾不存在就提示）、欄數 1–8、資料夾展開位置（附近 / 置中，config `panelOpenMode`）、桌面捷徑拖入模式、刪除模式、面板背景（自訂色用 comdlg32 `ChooseColor`）、「工作列」卡片（`PinAllFolders` 回呼 → `App.PinAllFoldersToTaskbar`）、版本 / 檢查更新（含「啟動時檢查更新」勾選）、GitHub 網址（`UpdateChecker.RepositoryUrl`）、「隱私」連結（資源字串 `SettingsPrivacyUrl`，依語言指到 README 的隱私段落；README 改標題要同步改）與「更新紀錄…」（`ChangeLogWindow`，內容是編進 exe 的 `CHANGELOG.md`（英文）或 `CHANGELOG.zh-TW.md`（繁中介面，由資源字串 `ChangeLogResource` 選；csproj `<EmbeddedResource LogicalName=… WithCulture="false">`，沒有 `WithCulture="false"` 的話 MSBuild 會把 `.zh-TW.md` 當成衛星組件資源），`ChangeLog.Parse` 只認 `##` / `###` / `- `；**兩份要一起維護**，`ChangeLogTests` 檢查版本與項目數一致）。卡片放在 `ScrollViewer`，視窗 `MaxHeight` = 工作區高度，螢幕太矮時捲動、確定 / 取消固定在下方。非模態、確定 / 取消；**非模態視窗的 `IsCancel` 不會自動關窗**，要自己 `Close()`。根目錄變更 → 重建 catalog 與 widget；其他設定 → 存檔、關閉已開面板。
- **Fluent 視窗**（設定、新增資料夾、更新）：`ThemeMode="System"`；卡片樣式 `Card` / `CardTitle` 在 `App.xaml`；`FluentWindow.UseOpaqueBackground` 關掉 Mica 改不透明底色，文字才能用 ClearType。
- **XAML 不可引用本組件型別**（例如 `x:Static` 到 `Strings`）：`Strings` 在 build 時才產生在 `obj/`，WPF 的暫時標記組件編譯不到。介面文字在 code-behind 設定。
- **App 圖示**：`tools/make-icon.ps1` 產生 `src/DeskGrid/Assets/DeskGrid.ico`（多尺寸 PNG ICO）。

## 介面語言（2026-11-03）

- **兩種語言、由使用者選**：英文與繁體中文。設定「語言」：*跟隨 Windows*（預設；Windows 介面語言為 zh-TW / zh-HK / zh-MO / zh-Hant 時用繁體中文，否則英文——簡體中文與其他語言都是英文）、*English*、*繁體中文*；config `language`。
- **切換方式**：`UiLanguage.Apply(AppLanguage)` 設定 `CultureInfo.DefaultThreadCurrentUICulture`、目前執行緒的 `CurrentUICulture`（WPF 自己的文字，例如文字框的剪下 / 複製 / 貼上來自 `zh-Hant` 衛星組件，也跟著變）與 `Strings.Culture`；啟動時載入 config 後立刻執行，並在 `App.ApplySettings` 再執行。文字在建立視窗、選單或對話框時才取，所以之後開啟的東西都用新語言；widget 只顯示資料夾名稱，而 widget 的右鍵選單和 widget 一樣長命，每次開啟時重新設定文字（`FolderWidget.LocalizeMenu`，2026-11-06；以前會停在建立時的語言）；已開的面板在按「確定」時關閉。`UiLanguage` 記住 Windows 自己的介面語言（在第一次 `Apply` 之前讀取），之後才能再選回「跟隨 Windows」。
- **政策**（細節見 `AGENTS.md`「語言政策」）：所有介面文字放 `Strings.resx` + `Strings.zh-TW.resx`（`UiLanguageTests` 檢查 key 與佔位符一致）；不可把 `Strings.X` 快取在 `static`；更新紀錄（`ChangeLogResource`）與隱私連結（`SettingsPrivacyUrl`）跟著語言；**所有進版控的 Markdown 都有 `NAME.md`（英文）+ `NAME.zh-TW.md`（繁體中文）兩個版本，結構相同**（`MarkdownPairsTests`）。
- 由 UI 執行緒顯示的 Windows 內建對話框（色彩選擇器、資料夾選擇器、`MessageBox` 的按鈕）的語言取自執行緒：`UiLanguage.Apply` 對 UI 執行緒呼叫 `SetThreadPreferredUILanguages`（「跟隨 Windows」時清除；測試 `WindowsDialogLanguageTests` 檢查 `GetThreadUILanguage`）。其他程式加進檔案總管右鍵選單的項目仍是 Windows 的語言。沒有在螢幕上看過另一種語言的色彩選擇器。
- 沒有實機驗證：執行中切換語言（單元測試驗證語言的選擇、key 一致與文件成對）。

## 版本檢查與更新

- **來源**：`https://api.github.com/repos/RT-Next-Energy-Systems/DeskGrid/releases/latest`（匿名，每小時 60 次上限夠用）；略過 draft / prerelease；tag 去掉 `v` 後與組件版本（csproj `<Version>`）比 major.minor.build（所以發行版本只用三段：第四段如 0.4.2.1 會被忽略，該版永遠不會被提示更新）。exe 資產用字尾 `-win-x64.exe` 找（舊 v0.1.0 資產叫 `DeskFolders-…`），校驗檔是 `<exe 名>.sha256`。
- **三個選項**：更新（下載 → 驗 SHA256（取自 release 資產的 API `digest` 欄位）→ `apply-update.ps1` 等本程序結束後 `Move-Item` 蓋掉 exe 再啟動；只有發布的單檔 exe 做得到，`UpdateInstaller.CanReplaceSelf`，開發版與無法寫入的資料夾改開 release 頁）、維持（config `skippedUpdateVersion` 記下該 tag，自動檢查不再提醒，手動檢查仍會彈）、取消（什麼都不存，下次啟動再提醒）。
- **觸發**：啟動後 8 秒自動檢查一次（網路失敗只記 log，不打擾）；設定視窗顯示目前版本與「檢查更新」按鈕，結果文字顯示在按鈕旁。
- **更新後顯示新功能**（2026-11-09）：config `lastSeenVersion`（上次顯示「新功能」的三段版本號）。widget 都出現後（`App.AfterFirstShow`，在首次執行的開機自啟詢問之後）`ShowWhatsNew` 拿它和 `UpdateChecker.CurrentVersion` 比：相同 → 什麼都不做；不同 → **先**存下目前版本再顯示視窗（當機不會讓它每次啟動都跳出來），而且除非是第一次執行（那時 `AutoStartAsked` 為 false；只記錄），只要 `ChangeLog.Since` 找得到內容就用 `ChangeLogWindow(seen, current)` 強制回應式開啟：內容是 `seen` 之後到執行中版本的各版（沒有 `lastSeenVersion` 時代的設定沒有 `seen`，只顯示執行中版本自己的那一段；降版沒有內容）。它與「設定 → 更新紀錄…」是同一個視窗、同一份內建更新紀錄，所以依介面語言顯示，外加一行提示（`WhatsNewTitle`、`WhatsNewHint`）。它看的是版本而不是更新方式，所以手動換檔更新也會顯示。每個新增功能的版本都有 `### 使用方法` 小節（見 `AGENTS.md`「更新紀錄與使用方法」），`ChangeLogTests` 對最新版本強制檢查。尚未在真實桌面上真的更新一次來看（視窗已在測試中以兩種語言建立並讀出內容）。
- **可關閉**（2026-10-30，SignPath Foundation 條款要求連網行為要揭露且能關）：設定視窗「DeskGrid 啟動時檢查更新」（config `checkForUpdatesAtStartup`，預設開）。關掉後 DeskGrid 不會自己連網，只有使用者按「檢查更新」或選「更新」才連 GitHub。README 的「隱私 / Privacy」列出所有網路行為，新增網路行為時要同步改。

## 刪除資料夾與疊圖示建資料夾（階段 8）

- **刪除**（`App.DeleteFolder`）：widget 右鍵「刪除資料夾…」→ 確認對話框 → `FolderDeleter.Delete`。資料夾本身一律進資源回收筒（`Shell/RecycleBin`，`SHFileOperation` + `FOF_ALLOWUNDO`；放不進資源回收筒時 shell 會先問，`FOF_WANTNUKEWARNING`）。config `deleteMode`（設定視窗）：`moveToDesktop`（預設，先把可見的檔案與子資料夾移到使用者桌面、同名加「 (2)」）/ `deleteItems`（連同資料夾進資源回收筒）。要移到另一個磁碟機的子資料夾（根目錄在 D:、桌面在 C:；`Directory.Move` 做不到）先整個複製再刪除（`FolderDeleter.CopyThenDelete`；複製失敗時刪掉複製到一半的，原資料夾保留，2026-11-06）。有任何項目移不過去 → **不刪資料夾**並顯示原因。桌面 widget 由 scanner 的 Deleted 事件移除，config 殘留由 `Reconcile` 清掉，不必另外處理。
- **疊圖示**（`Widgets/IconMerger`，使用者選定 2026-10-29）：把桌面圖示拖到另一個桌面圖示上 → 新資料夾（名稱 `WidgetFolderName.Suggest`）取代被放上的圖示位置，內含兩者（`WidgetBoard.MergeIcons` → `ItemImporter.Import`，規則同拖到 widget）。Explorer 自己把「放到另一個捷徑上」當「用它開啟」，所以拖曳期間要自己接：
  - **偵測**：`DesktopIconWatcher` 的 WinEvent 範圍加上 `EVENT_SYSTEM_CAPTURESTART`（0x8，實測點一下桌面圖示會依序來 0x8 / 0x9）→ `DesktopHost.DesktopPressed`。`IconMerger` 在按下當下讀圖示格（游標在某個圖示格內才繼續），之後每 20 ms 看左鍵（`GetAsyncKeyState`，左右鍵互換時看右鍵）與位移是否超過 `SM_CXDRAG`，超過才算開始拖曳；放開後 500 ms 收掉接收區。
  - **接收區**：**只用一個**透明 `DesktopSurface`（`AllowDrop` 的 `Border`，背景 alpha=1）跟著游標所在圖示的範圍（圖示格頂端到圖示圖片底部，下面的名稱留給 Explorer）移動，游標不在任何圖示上時停到螢幕外（-30000）。每個圖示一個視窗行不通：實測一個 `HwndSource` 掛到桌面要約 26 ms，40 個圖示會凍結約 1 秒。實測（真實 Explorer）：alpha=1 的接收區**看不見但會被 `WindowFromPoint` 命中**，alpha=0 則穿透，所以不能用 0。
  - **圖示 → 檔案**：`LVM_GETITEMTEXTW`（跨行程，`DesktopIconGrid.ReadNames`，與讀位置共用 `RemoteBuffer`）取得圖示下方的名稱，再用 `DesktopFiles.ByDisplayName` 對應到使用者 / 公用桌面的檔案（名稱用 `ShellItem.GetDisplayName`，與 Explorer 一致）；同名（兩個桌面都有、或副檔名隱藏後撞名）不對應，該圖示不接受疊放。本機、資源回收筒等虛擬圖示對應不到檔案，也不接受。桌面上的**真資料夾**也不接受（保留 Explorer 原本「拖進資料夾」的行為）。
  - **不可在拖曳回呼裡呼叫 Explorer**：DragEnter / DragOver / Drop 都是 Explorer 的 OLE 迴圈同步 RPC 進來的，此時對 Explorer 的 list view 送訊息可能互等。所以圖示讀取在按下當下 / 開始拖曳時做，回呼裡只用已讀好的資料。
  - **位置**：新資料夾的 config 位置先設成被放上圖示的格，再匯入項目，之後 scanner 看到資料夾時 `WidgetBoard.Add` 吸到最近空格（圖示被移走 = 就是那一格；沒移走〔複製模式、非捷徑〕= 旁邊最近的空格）。
  - 回報給來源的效果一律 Copy（同 widget 拖放）；一個項目都沒加進去 → 刪掉剛建的空資料夾、回報 None。
- **桌面上不再有「新增資料夾」磚**（2026-10-29 使用者要求移除）：新增資料夾走系統匣「新增資料夾…」或疊圖示；原本「把檔案拖到磚上建資料夾」由疊圖示取代，config 的 `addTilePosition` 已移除（舊 config 裡的值載入時被忽略）。
- **拖 widget 到資源回收筒**（2026-10-30）：`WidgetBoard.Entry` 的 `DragCompleted` 先問 `IsOverRecycleBin`（游標所在的桌面圖示格 `DesktopIconGrid.IconAt`，且圖示下的名稱 = `RecycleBin.DisplayName`，即 `::{645FF040-…}` 的 Shell 顯示名稱，跟顯示語言與改名走）。是 → widget 回到拖曳前的格子，再 `Dispatcher.BeginInvoke` 觸發 `DeleteRequested`（走原本「刪除資料夾…」確認，離開滑鼠事件處理再問）。實機（explorer 啟動的行程）驗證：名稱 `資源回收筒` 與桌面圖示一致、游標在圖示上才命中；沒有實際拖放（電腦使用權限不能拖）。**注意**：從 Claude 桌面 App 的 PowerShell 讀 Shell 名稱會是英文（行程 UI 語言不同），不能拿來比對。
- **面板開著時拖 widget**：`WidgetBoard.WidgetDragStarted` → `PanelController.Close()`（原本面板留在 widget 的舊位置）。
- **測試平行**：顯示視窗 / 選單 / 取 Shell 圖示的測試類別放在 `ShellCollection`（不平行）：同時只能有一個選單（第二個會關掉第一個）、別的測試視窗會搶走啟用讓面板收合。
- **測試注意**：從 Claude 桌面 App（MSIX）啟動的行程（含 `dotnet test`）**不能把視窗掛到 Explorer 的桌面底下**（`HwndWrapper` 建立失敗，錯誤碼 0），所以 `DesktopSurface` / `IconMerger` 無法寫成單元測試；要驗證桌面行為，只能在真正的 DeskGrid 行程（用 `explorer.exe <exe>` 啟動）裡加暫時的探測碼並讀 log。

## 讓腳本與 AI agent 控制（2026-11-08）

- **原因 / 內容**（使用者問 DeskGrid 能否讓 AI agent 操作，選了「CLI + pipe」方案）：`DeskGrid.exe --ctl <指令> [參數]` → 連一次 `\\.\pipe\DeskGrid.Control.<使用者>`（`InOut`、`CurrentUserOnly`），請求 `{"command":"…","args":[…]}`，回覆 `{"ok":true,"result":…}` / `{"ok":false,"error":"…"}`，各一行。工作列按鈕用的舊單向 pipe `DeskGrid.Open.<使用者>` 不變。
- **指令**（唯一清單是 `ControlCommands.Usage`；CLI 說明、README 表格與錯誤訊息都由它而來）：`status`、`list [資料夾]`、`new`、`rename`、`add <資料夾> <路徑>...`（`ItemImporter`，與拖放規則相同，一次一個路徑，好逐一回報加入 / 略過）、`move <資料夾> <項目> <位置>`、`open [資料夾]`（開在 widget 旁，`WidgetBoard.BoundsOf`；已開啟則不動作）、`close`。**沒有刪除指令**（使用者還沒決定要不要破壞性操作；刪除資料夾或項目仍只能從介面操作並經確認）。會改變內容的指令會呼叫 `WidgetCatalog.Rescan()`，讓回覆和下一次 `list` 已看得到變更。
- **需使用者開啟**：config `allowControl`（預設 false），設定的勾選框「允許腳本與 AI agent 控制」。pipe 一直在監聽；關閉時每個指令都回錯誤，說明怎麼開啟。這不是網路行為，但 README「隱私」有提到。
- **機器介面用英文**：協定本身的訊息和 `--ctl help` 只有英文（是 API，不是 UI；對 agent 要穩定）。來自共用程式碼的錯誤（`WidgetFolderName.Validate`）跟隨介面語言。JSON 用預設 encoder 輸出 → 非 ASCII 寫成 `\uXXXX`，任何主控台字碼頁都不會亂碼。
- **視窗程式的輸出**：`ControlCli.OpenOutput` 有繼承到 stdout（pipe / 檔案，例如 Bash 或 `| Out-String`）就寫進去；否則 `AttachConsole(ATTACH_PARENT_PROCESS)`。互動式命令提示字元不會等 GUI exe 結束，所以文件說要接管線。client 在單一執行個體 mutex 之前就結束，不會變成一個 DeskGrid。
- **client 讓出前景權**（`AllowSetForegroundWindow(ASFW_ANY)`），`open` 才能讓面板取得焦點；否則面板第一次失去焦點就會關閉。
- **真機驗證**（以 explorer.exe 啟動 dev build，config 用 explorer 執行的腳本開啟）：從 Bash 與 PowerShell 執行 `status`、`list`、`new`（中文名稱）、`add`（notepad.exe → `notepad.lnk`、文字檔 → `.txt.lnk`、不存在的路徑回報為略過）、`move`、`rename`、`open`（面板顯示、在前景、項目依 move 後的順序，截圖確認）、`close`，以及錯誤情況 / 結束代碼。設定視窗的勾選框以真正的 `SettingsWindow` 用兩種語言（深色主題）繪製成圖檢查無誤；`SettingsWindowTests.AllowControl_ShowsTheSetting_AndOkReportsItFlipped` 檢查它帶入帶出正確。

## 鍵盤快速鍵（2026-11-08）

- **內容**（使用者要「簡單、易用、直觀、可自訂的快速鍵系統」，例如 Ctrl+M 開啟所有資料夾，像釘在工作列的那顆按鈕）：用 `RegisterHotKey` 做系統層級快速鍵（`HotkeyService`；不管哪個程式在前景都有效；加 `MOD_NOREPEAT`，按住不放不會連發）。動作：`allFolders`（預設 Ctrl+M；切換所有資料夾的列表）、`newFolder`、`settings`（無預設），以及每個資料夾一個（`folder:<名稱>`，開啟該資料夾的面板）。按下 → `App.RunHotkey` → 面板走和工作列按鈕同一條 `OpenPanel(OpenMessage)`，以游標為錨點，所以「再按一次收合」「資料夾已刪除 → 改開列表」「widget 還沒好 → 先等」都直接沿用。
- **預設是單純的 Ctrl+M**（使用者在我提議 Ctrl+Alt+M 時選了 Ctrl+M）：DeskGrid 執行期間，全域的 Ctrl+M 會從所有其他程式手中被搶走（終端機裡是 Enter，編輯器裡是縮排）。README 有說明，並建議可改用 Ctrl+Alt+M。
- **儲存**：一般動作在 `AppConfig.Hotkeys`（動作 id → `"Ctrl+M"`；一開始就有預設值，檔案裡的項目取代預設，`""` = 使用者清除，沒有該項 = 用預設，所以要用 `JsonObjectCreationHandling.Populate`）；資料夾的快速鍵存在 `WidgetConfig.Hotkey`，因此資料夾改名時會跟著走（掃描器本來就會搬 widget 設定），資料夾刪除時一併消失。不是可用快速鍵的文字視為沒有。
- **規則**（`Hotkey.IsValid`）：必須包含 Ctrl、Alt 或 Win（單用 Shift 會擋掉大寫字母）；主鍵是字母、數字、F1–F24、空白鍵、Enter、方向鍵、Home / End / PageUp / PageDown / Insert / Delete（刻意不含 Tab 與 Esc：錄製時它們是切換焦點 / 關閉視窗）。兩個動作不能共用一組（視窗裡的 `HotkeyBox.Check`）；別的程式已經用了則表現為註冊被拒絕。
- **套用**：`App.ApplyHotkeys` 在啟動時、每次目錄更新後（改名 / 刪除會改變資料夾的 id）、快速鍵視窗按「確定」後執行。清單和已套用的相同時 `HotkeyService.Apply` 什麼都不做。被拒絕的會寫 log；只有在視窗按「確定」之後才跳訊息列出它們（`HotkeysTaken`），免得別的程式佔用的組合每次啟動都來吵。上一次按下開出的對話框還沒關時，再按一次會被忽略（`_runningHotkey`）。
- **錄製**：`HotkeysWindow`（強制回應視窗，從「設定 → 鍵盤快速鍵 → 自訂…」開啟）每個動作一個 `HotkeyBox`（點一下、按下組合鍵；Backspace / Delete 或 × 清除；Tab 與 Esc 照常）和「還原預設值」。視窗開著時生效中的快速鍵會被**暫停**（`HotkeyService.Suspend`），否則 Windows 會把正要錄製的按鍵搶走。列是用程式碼建的，因為 XAML 不能參照 `HotkeyBox`。
- **已在真機驗證**（2026-11-08，dev build 用 explorer.exe 啟動；用 PowerShell 的 `keybd_event` 注入按鍵、UI Automation 讀視窗、`--ctl status` 讀面板狀態）：config 裡的快速鍵（Ctrl+Q，是使用者自己在視窗裡設的）能開出列表且 DeskGrid 拿到前景，再按一次收合、前景還回去；資料夾快速鍵（Ctrl+Alt+1）、設定（Ctrl+Alt+S）、新增資料夾（Ctrl+Alt+N）都有效；新增資料夾對話框開著時再按一次不會疊出第二個；資料夾改名後 Ctrl+Alt+1 仍能開它（透過 `--ctl`，見下）；用鍵盤在視窗裡錄製（組合被佔用、單純字母會被拒絕並說明原因、重複會指出哪個動作已有、Backspace 清除、錄到 Alt+F1），按「確定」即時生效（新快速鍵馬上能用，暫停的舊快速鍵恢復）。未驗證：按「確定」時別的程式佔用組合的訊息、Win 鍵組合（Windows 保留很多）、預設的單純 Ctrl+M 本身（使用者的 config 用的是 Ctrl+Q）。
- **改名與控制管道**（測試時發現）：`--ctl rename` 在搬完資料夾後立刻 `Rescan()`，這時監看器的改名事件還沒到，`Reconcile` 把舊資料夾的設定當成消失的資料夾丟掉（位置、項目順序、快速鍵全沒了）。現在 `App.RenameFolder` 在搬完後立刻呼叫 `WidgetCatalog.MoveSettings(舊, 新)`；監看器稍後的事件就沒有東西可搬（`WidgetCatalogTests.RescanRightAfterRenamingAFolder_KeepsItsSettings`）。

## 發布與 CI（階段 7）

- **GitHub**：公開 repo `RT-Next-Energy-Systems/DeskGrid`（2026 年 11 月由 `Ryan970714/DeskGrid` 搬過來；GitHub 會把舊位址轉址，0.4.1 以前的版本檢查更新仍用舊位址），預設分支 `main`，MIT（著作權人 Ryan Kuo）。README 英文（`README.md`）+ 繁中（`README.zh-TW.md`），內容需保持一致。其中的截圖在 `docs/images/`（`NAME.png` + `NAME.zh-TW.png`），由真正的 UI 類別配上 Windows 內建 App 繪製，不是從桌面截取（做法見 `context/decisions.md` 2026-11-06（二））；視窗改了就要重畫。
- **打包**：`Properties/PublishProfiles/win-x64.pubxml`：self-contained、PublishSingleFile、`IncludeNativeLibrariesForSelfExtract`（WPF 原生 DLL 也包進 exe，首次執行解到 %TEMP%）、壓縮、無 pdb。WPF 不支援 trimming。`SatelliteResourceLanguages=en;zh-Hant;zh-TW` 只帶用得到的語系（WPF 自己的文字如文字框右鍵在 `zh-Hant`），exe 65.1 → 61.9 MB。`.gitignore` 範本會忽略 `*.pubxml`，已為這個 profile 加例外。
- **CI**（`.github/workflows/ci.yml`）：push 到 main 與 PR → windows-latest、.NET 10.0.x → build、test、publish，exe 上傳為 artifact。
- **Release**（`release.yml`）：推 `v*` tag → test → 以 tag 版本號 publish → `gh release create`，只附 `DeskGrid-<版本>-win-x64.exe`。csproj 的 `<Version>` 是本機建置的預設值。
- **未簽章**：README 說明 SmartScreen「其他資訊 → 仍要執行」與 SHA256 驗證。
- **程式碼簽章準備**（SignPath Foundation，2026-10-30；**尚未申請**，表單與帳號由使用者自己填）：條款見 <https://signpath.org/terms>（草稿，申請前再讀一次）。repo 端已備妥：README（en / zh-TW）的「Privacy / 隱私」與「Code signing policy / 程式碼簽章政策」（目前寫「尚未簽章、打算申請」，核准後要把聲明改成正式生效、並把同一段放上 Release 頁）；csproj `IncludeSourceRevisionInInformationalVersion=false`（產品版本不帶 `+commit`，條款要求產品名稱 / 版本固定，`AssemblyMetadataTests` 守著）；自動檢查更新可關。**核准後**才做：SignPath 建 project、加 GitHub.com trusted build system、artifact configuration（`zip-file` 內含 exe，並強制 product name / version）、signing policy（手動核准）、API token 存 GitHub secret `SIGNPATH_API_TOKEN`；`release.yml` 改成 `actions/upload-artifact` → `signpath/github-action-submit-signing-request`（`wait-for-completion`）→ `gh release create` 附簽好的 exe；Release 說明要放「Code signing policy」連結。簽章後要在開著 Smart App Control 的機器實測，因為沒有來源明確保證 SignPath 憑證一定被 SAC 放行。
- 推送 `.github/workflows/` 需要 gh token 的 `workflow` 權限（`gh auth refresh -h github.com -s workflow`）。

# 行為規則（必須遵守）

- 拖 `.exe` 進資料夾 → 建立 `.lnk`，**禁止移動或複製 exe 本體**
- 拖桌面捷徑進資料夾 → 預設「移動」，設定可改「複製」
- 支援項目：`.lnk` / `.url` / `.exe` / 一般檔案
- 啟動一律用 ShellExecute
- Per-Monitor V2 DPI aware（MVP 先確保單螢幕正確，多螢幕列為後續）
- 介面文字放資源檔，支援英文與繁體中文（zh-TW），使用者可在設定選擇語言；**所有進版控的 Markdown 都有兩種語言版**（`NAME.md` + `NAME.zh-TW.md`）——見 `AGENTS.md`「語言政策」

# 開發階段與進度

一次只做一個階段，完成後停下等使用者驗證。

| # | 階段 | 狀態 |
|---|---|---|
| 1 | Spike：DesktopHost 驗證（純色方塊掛桌面；所有視窗後方、Win+D 後可見、重啟 explorer 自動恢復） | ✅ 2026-10-24 使用者驗證通過 |
| 2 | ConfigStore + FolderScanner + FileSystemWatcher | ✅ 2026-10-24 使用者驗證通過 |
| 3 | IconService + 預覽合成 | ✅ 2026-10-24 使用者驗證通過 |
| 4 | FolderWidget（顯示、拖動、存位置） | ✅ 2026-10-24 使用者驗證通過 |
| 5 | PopupPanel（格子、啟動、收合） | ✅ 2026-10-25 使用者驗證通過 |
| 6 | 拖放加入、TrayApp、開機自啟 | ✅ 2026-10-25 使用者驗證通過 |
| 7 | GitHub Actions CI、self-contained 打包、README（含 SmartScreen 未簽章說明）、LICENSE (MIT) | ✅ 完成 2026-10-25，已推上 GitHub、CI 通過、v0.1.0 已發布，待使用者驗證 |
| 8 | 刪除資料夾（右鍵 + 設定）、桌面圖示疊圖示建資料夾、修 bug | 完成 2026-10-29（建置 / 146 測試通過）；右鍵刪除與疊圖示拖曳因工具限制未能實機操作，待使用者驗證 |
| 9 | 面板項目的 Windows 原生右鍵選單、面板 / widget 右鍵「新增 / 刪除資料夾」、開機自啟流程（`--autostart`、等桌面排好、第一次詢問）、README 的應用程式控制封鎖說明、修 bug | 完成 2026-10-30（建置 / 168 測試通過，v0.3.1 已發布；含拖資料夾到資源回收筒刪除，使用者實測成功）；原生選單以真實 `IContextMenu` 做了整合測試（顯示、阻塞、關閉），但沒有實際點選指令（電腦使用權限不含 DeskGrid 視窗），待使用者驗證。2026-10-31 修拖曳與「拖曳時自動開 / 關資料夾」（181 測試），拖曳情境待使用者實測；v0.3.3 發布（2026-10-31，含拖曳修正、啟動檢查更新開關、隱私連結）；v0.3.4 發布（2026-11-01，多螢幕修正）；v0.3.5 發布（2026-11-02，穩健性修正：捷徑掃描、config 鎖定、未處理例外、更新對話框） |
| 10 | 介面語言選擇（英文 / 繁體中文）、雙語文件政策、面板展開位置（附近 / 置中） | 完成 2026-11-03（建置 / 測試通過）；沒有實機驗證：執行中切換語言、置中展開面板在真實桌面上的樣子。2026-11-06：全程式碼找 bug，修正 10 項（見 `context/decisions.md` 2026-11-06），307 測試通過；Fluent 視窗以兩種語言、兩種主題繪製成圖檢查無誤，修正本身沒有在真實桌面上試過 |
| 11 | 讓腳本與 AI agent 控制（`--ctl` CLI + 控制 pipe，需在設定開啟） | 完成 2026-11-08（建置 / 345 測試通過）；每個指令都從 Bash 與 PowerShell 對真機 dev build 執行過，面板開啟有在畫面上確認；等待使用者驗證 |
| 12 | 鍵盤快速鍵（系統層級，每個動作與每個資料夾都能自訂） | 完成 2026-11-08（建置 / 374 測試通過）；已在真實桌面按下快速鍵並使用快速鍵視窗（見「鍵盤快速鍵」）；與階段 11 一起定為 0.5.0（tag 由專案擁有者推） |

# 工作方式

- 使用 Git，每階段完成 commit 一次，訊息用英文 conventional commits
- 每階段結束告訴使用者：做了什麼、如何手動測試、已知問題
- 不確定的設計決策先問使用者，不要自行假設
- 每完成一階段更新本檔「開發階段與進度」

# Session 結束流程

> 備註：第 1–3、6 項的檔案僅存在作者本機，不會出現在公開 repo。

每次工作完成，依序執行：

1. 更新 `context/current-task.md`
2. 更新 `context/progress.md`
3. 更新 `context/todo.md`
4. 若有架構修改／設計修改／重要決策 → 新增一筆到 `context/decisions.md`（只能新增，不得覆蓋既有紀錄）——**兩種語言版都要**（`decisions.md` 與 `decisions.zh-TW.md`）
5. 若專案摘要改變 → 更新 `context/project-summary.md`（保持精簡，1–3 頁，不寫工作日誌）——**兩種語言版都要**
6. 新增一筆到 `context/session-log.md`（最新放最上方，每筆不超過 10 行）

修改 `CLAUDE.md`、`AGENTS.md`、`README`、`CHANGELOG` 時同樣兩種語言一起改（見 `AGENTS.md`「語言政策」）。

完成後輸出：**本次完成內容**、**如何手動測試**、**已知問題**、**下一步建議**。不要重新輸出所有 Markdown 內容。

# Context 管理原則

- 聊天內容是短期記憶，Markdown 是永久記憶。所有長期資訊必須寫入本檔或 `context/*.md`，不要依賴聊天歷史。
- 新 Session 應能只靠閱讀上述 Markdown 檔案就恢復完整工作狀態。
- 需要背景時優先引用 Markdown，避免在聊天中重複專案背景、架構、技術棧、或重複描述已完成工作——Markdown 永遠優先於聊天紀錄。
