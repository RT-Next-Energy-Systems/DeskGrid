# Decisions

> 只能新增，不得覆蓋或刪除既有紀錄。若某個決策後來被推翻，新增一筆新紀錄說明並引用原紀錄的日期，不要修改原紀錄。

---

### 2026-10-24 — 目標框架改用 .NET 10 LTS
**Decision**: TargetFramework 用 `net10.0-windows`，SDK 以 winget 安裝 Microsoft.DotNet.SDK.10。
**Reason**: 原規格 .NET 8 於 2026-11-10 結束支援；.NET 10 LTS 支援到 2028-11。
**Alternatives**: 維持 .NET 8（17 天後無安全更新）。
**Consequences**: CLAUDE.md 技術棧更新；`global.json` 鎖 10.0.x。
**Author**: 使用者決定

---

### 2026-10-24 — Widget 掛在 SHELLDLL_DefView、圖示之上
**Decision**: surface 為 `SHELLDLL_DefView` 的子視窗，z-order 在 `SysListView32` 之上。
**Reason**: widget 要能點擊、拖動；放在圖示後方（wallpaper engine 的 WorkerW 做法）會被圖示 list view 攔截所有滑鼠輸入。掛 DefView 而非其父視窗：DefView 在新舊階層都存在，且在舊版被 Explorer 搬到 WorkerW 時 widget 會跟著走。
**Alternatives**: 掛在 WorkerW（圖示後方，無輸入）；掛在 Progman/WorkerW 容器（舊版 DefView 被搬走時會被留在後方）；top-level HWND_BOTTOM 視窗（Win+D 時會被蓋掉，需額外偵測）。
**Consequences**: widget 會蓋住其下方圖示；「隱藏桌面圖示」時 widget 仍顯示。
**Author**: Claude（依規格「掛在桌面圖示層」推論）

---

### 2026-10-24 — Surface 使用 layered 子視窗（UsesPerPixelTransparency）
**Decision**: `HwndSource` + `WS_CHILD` + `UsesPerPixelTransparency`（→ `WS_EX_LAYERED`）。
**Reason**: 24H2+ Progman 帶 `WS_EX_NOREDIRECTIONBITMAP`，非 layered 子視窗不會被畫出；同時取得逐像素透明與透明處點穿。
**Alternatives**: WPF `Window` + `SetParent`（Window 不適合當子視窗，layered 位置處理易出錯）。
**Consequences**: WPF 以軟體渲染 + UpdateLayeredWindow；小尺寸 widget 效能可接受。
**Author**: Claude

---

### 2026-10-24 — 重新掛載 = 重建 HwndSource
**Decision**: 每次（重新）掛載都建立新的 `HwndSource`，沿用同一個 WPF content 物件。
**Reason**: 實測 explorer 結束時跨行程子視窗會被銷毀；單一程式路徑最簡單，且 content 狀態保留。
**Alternatives**: 對仍存活的 HWND 做 `SetParent`（多一條路徑，實測不會發生）。
**Consequences**: 偵測點為 `HwndSource.Disposed`（→ `DesktopSurface.Lost`）＋ `TaskbarCreated`，以 1 秒 `DispatcherTimer` 重試到成功。
**Author**: Claude

---

### 2026-10-24 — Win32 互通用 CsWin32
**Decision**: P/Invoke 由 CsWin32 產生（`NativeMethods.txt`）。
**Reason**: 規格建議；後續階段需要 COM（IShellItemImageFactory、IShellLink），CsWin32 可直接產生。
**Alternatives**: 手寫 `LibraryImport`。
**Consequences**: `AllowUnsafeBlocks=true`；CsWin32 未含 `HWND_TOP` 等巨集，需自行定義。
**Author**: Claude

---

### 2026-10-24 — 文件語言
**Decision**: CLAUDE.md / AGENTS.md / context 用繁體中文；程式碼、註解、commit 用英文。
**Reason**: 與使用者規格語言一致；開源程式碼維持英文。
**Alternatives**: 全英文。
**Consequences**: 階段 7 的 README 以英文撰寫。
**Author**: Claude

---

### 2026-10-24 — 階段 2 config 語意
**Decision**: widget 位置存實體螢幕像素（載入時跑出工作區就夾回）；面板欄數為全域一個設定；重掃時清除已不存在資料夾/項目的設定（換根目錄等同全新開始）；啟動時根目錄不合格 → 提示後改用預設並寫回 config。
**Reason**: 實體像素與 `DesktopSurface` 一致，改縮放比例時位置不跑；全域欄數像手機資料夾且不需 per-widget UI；config 只反映現存資料夾。
**Alternatives**: DIP / 工作區相對比例；per-widget 欄數；永久保留殘留設定；提示後結束程式。
**Consequences**: 刪除後從資源回收筒還原的資料夾會回到預設位置。
**Author**: 使用者決定

---

### 2026-10-24 — 掃描與 config 協調拆成 FolderScanner + WidgetCatalog
**Decision**: `FolderScanner` 只管檔案系統（掃描、watcher、debounce、rename 事件）；`WidgetCatalog` 把掃描結果與 config 合併（排序、清除、rename 轉移、存檔）。
**Reason**: 單一職責；合併邏輯（`Reconcile`）是純函式，可不碰磁碟直接測。
**Alternatives**: FolderScanner 直接依賴 ConfigStore 一手包辦。
**Consequences**: 之後的 UI 只訂閱 `WidgetCatalog.Changed`。
**Author**: Claude

---

### 2026-10-24 — 事件序列化靠 SynchronizationContext，debounce 後整個重掃
**Decision**: `FolderScanner` 捕捉建立時的 `SynchronizationContext`（沒有就丟例外），所有事件依序 post 回去；debounce 結束後整個根目錄重掃，不做增量更新。
**Reason**: config 只在 UI 執行緒存取，不需鎖；rename 一定先於後續 refresh。資料量小（數十資料夾 × 數十項目），重掃成本可忽略且不會漏事件。
**Alternatives**: 背景執行緒 + lock；依事件做增量更新。
**Consequences**: 測試需在 WPF Dispatcher 執行緒上建立 scanner（`DispatcherThread` helper）。
**Author**: Claude

---

### 2026-10-24 — 項目範圍與預設排序
**Decision**: 項目 = widget 資料夾內的可見檔案（略過隱藏/系統檔、子資料夾、根目錄散檔）；首次依 Explorer 自然排序，之後新項目接在最後。
**Reason**: 規格支援 .lnk/.url/.exe/一般檔案，不含資料夾；desktop.ini、Office 暫存檔不該出現；接在最後符合手機加入 App 的直覺。
**Alternatives**: 依名稱或修改時間每次重排；子資料夾也顯示為項目。
**Consequences**: 目前沒有手動重排 UI，排序只由加入順序決定。
**Author**: Claude（慣例預設，已於階段回報告知使用者）

---

### 2026-10-24 — 測試框架 xUnit v3
**Decision**: `tests/DeskGrid.Tests`，xUnit v3 3.2.2 + VSTest runner；`Log.FilePath` 可由測試改寫，避免寫進真實 log。
**Reason**: 開源專案常見；Config/Folders 是純邏輯，適合自動測試。
**Alternatives**: MSTest、NUnit；只做手動測試。
**Consequences**: `InternalsVisibleTo("DeskGrid.Tests")`；共用設定移到 `Directory.Build.props`。
**Author**: Claude

---

### 2026-10-24 — 項目一律顯示圖示，不用內容縮圖
**Decision**: `SIIGBF_ICONONLY`；圖片、PDF、影片也顯示檔案類型圖示。
**Reason**: 照規格「取圖示」；全部正方形、風格一致像手機資料夾；不觸發縮圖產生，較快。
**Alternatives**: `SIIGBF_RESIZETOFIT`（有縮圖就用，非正方形需加邊）。
**Consequences**: 看不到圖片內容。
**Author**: 使用者決定（附實測對照圖）

---

### 2026-10-24 — Shell 圖示以直通 alpha 解讀
**Decision**: 從 DIB section 複製像素，以 `PixelFormats.Bgra32` 建立。
**Reason**: 實測多個圖示有 RGB > A 的像素，預乘格式不可能出現，故為直通 alpha；`CreateBitmapSourceFromHBitmap` 會丟 alpha。
**Alternatives**: `Pbgra32`（邊緣會偏亮，alpha=0 但有色的像素會變成可見雜訊）。
**Consequences**: 若某些 Shell handler 回傳預乘資料，邊緣會略暗（未觀察到）。
**Author**: Claude

---

### 2026-10-24 — IconService 執行緒與快取
**Decision**: Shell 呼叫與合成在單一專用 STA 執行緒；快取只放記憶體，以 (路徑, LastWriteTimeUtc) 為 key 並快取 `Task`；預覽依前 4 項的 key 決定是否重建；`Retain` 隨 catalog 刷新清除。
**Reason**: Shell handler 可能很慢，不可卡 UI；frozen bitmap 可跨執行緒；mtime 讓「檔案變動才重建」不需要額外事件。
**Alternatives**: UI 執行緒同步取圖；磁碟快取；多執行緒取圖。
**Consequences**: 啟動時每個項目要向 Shell 取一次；捷徑目標圖示變了但 .lnk 沒變時不會更新。
**Author**: Claude

---

### 2026-10-24 — Widget 外觀、尺寸、預設位置、對齊格線
**Decision**: 淺色半透明圓角方塊；48 DIP（與原生中型圖示同大）；新 widget 從主螢幕工作區右上角往下排；放開後對齊格線。
**Reason**: 使用者看過對照圖（A/B/C × 48/64/80）後選定。
**Alternatives**: 深色半透明 / 無底框；64、80 DIP；左上角 / 螢幕中央；自由擺放。
**Consequences**: 內部小圖示約 18 DIP；右側有原生圖示時會疊在一起。
**Author**: 使用者決定

---

### 2026-10-24 — 格子定義與衝突處理
**Decision**: 格子 = widget 佔位 76×96 DIP（依螢幕 DPI 換成實體像素），錨定工作區右上角；一格一個 widget；載入與放開都吸到「最近的空格」。
**Reason**: 76×96 與原生圖示格距接近；同一規則處理拖放、載入、DPI 變更後的舊座標，位置永遠在格上且不重疊。
**Alternatives**: 允許重疊；放到有人的格子時交換位置；錨定左上角。
**Consequences**: 放到有人的格子會跑到次近空格；格子與 Windows 原生圖示格不對齊（原點不同）。
**Author**: Claude（使用者選「對齊格線」後的實作細節，已於回報告知）

---

### 2026-10-24 — 暫時性結束入口
**Decision**: 階段 4–5 期間，widget 右鍵選單提供「結束 DeskGrid」。
**Reason**: 移除 spike 後在系統匣（階段 6）之前沒有結束方式；右鍵 → 選單比直接右鍵結束不易誤觸。
**Alternatives**: 只能用工作管理員結束；提前做系統匣。
**Consequences**: 階段 6 要移除，字串 `MenuExit` 會沿用到系統匣選單。
**Author**: Claude

---

### 2026-10-24 — 面板位置、大小、標題、可自訂背景
**Decision**: 面板開在 widget 旁；項目 48 DIP；顯示資料夾名稱標題；背景可自訂：三種預設（system 毛玻璃跟隨系統〔預設〕/ dark / light）+ custom（顏色 + 不透明度）；config 新增 `panelBackground`（超出原規格 5 項）；設定 UI 放階段 6。
**Reason**: 使用者選定與同意。
**Alternatives**: 螢幕中央；64 DIP；無標題；固定單一背景。
**Consequences**: 階段 6 設定視窗需包含背景選擇與顏色/不透明度。
**Author**: 使用者決定

---

### 2026-10-24 — 面板行為預設
**Decision**: 最多顯示 4 列後捲動；點項目啟動後收合；再點同一 widget 關閉、點另一 widget 直接切換；同時一個面板；項目名稱用 Shell 顯示名稱；空資料夾顯示提示文字。
**Reason**: 慣例（手機資料夾 / 系統彈出選單）；Shell 顯示名稱與檔案總管一致。
**Alternatives**: 依螢幕高度決定列數；啟動後保持開啟；檔名直接顯示。
**Consequences**: 無。
**Author**: Claude（已於階段回報告知）

---

### 2026-10-25 — Acrylic 面板的視窗做法
**Decision**: Acrylic 樣式用 `SingleBorderWindow` + `WindowChrome(GlassFrameThickness=-1, CaptionHeight=0)`，拿掉 `WS_SYSMENU`，自己 Measure 內容設定 Width/Height（不用 SizeToContent）；其他樣式用 layered 視窗。
**Reason**: 實測無邊框 `WS_POPUP` 拿不到 system backdrop；有 `WS_SYSMENU` 時 DWM 會在延伸框架畫關閉鈕；SizeToContent 會多算已被移除的外框，視窗過大且超出螢幕。
**Alternatives**: 自己模擬模糊（layered 無法取樣背後內容）；只提供純色。
**Consequences**: Acrylic 與 layered 必須在建立視窗前決定，因此每次開面板都建新視窗（也順便讀到最新設定）。
**Author**: Claude

---

### 2026-10-25 — 桌面 surface 不啟用桌面（MA_NOACTIVATE）
**Decision**: `DesktopSurface` 處理 `WM_MOUSEACTIVATE` 回 `MA_NOACTIVATE`。
**Reason**: 實測面板開著時點 widget，啟用 Explorer 桌面的那次點擊被 Explorer 吞掉，widget 收不到按下（點另一個 widget 要點兩次）。不啟用桌面後，面板的開關完全由 `PanelController` 決定，不再需要「關閉後 500ms 不重開」的防護。
**Alternatives**: 保留桌面啟用 + 計時防護 + 重新捕捉滑鼠（試過，按下根本沒送達，無效）。
**Consequences**: 點 widget 不會讓桌面成為前景視窗；面板開著時拖曳 widget 不會關閉面板。
**Author**: Claude

---

### 2026-10-25 — 啟動項目用 Process.Start(UseShellExecute)
**Decision**: 以 `Process.Start` + `UseShellExecute=true`（內部即 ShellExecuteEx）啟動；工作目錄 = 項目所在資料夾；`ErrorDialog=true`。
**Reason**: 符合「一律用 ShellExecute」，比手寫 SHELLEXECUTEINFO 簡潔；行為與檔案總管雙擊一致；錯誤由 Windows 自己顯示。
**Alternatives**: CsWin32 直接呼叫 `ShellExecuteEx`。
**Consequences**: 失敗（含 UAC 取消）只記 log。
**Author**: Claude

---

### 2026-10-25 — 拖放規則與開機自啟方式
**Decision**: 一般檔案 → 建捷徑；非桌面的 `.lnk`/`.url` → 一律複製；資料夾 → 建捷徑；`.exe` → 建捷徑（規格）；桌面捷徑 → 依拖入模式（規格）。開機自啟用 HKCU Run 機碼。
**Reason**: 使用者選定；建捷徑不會意外搬走使用者的文件；不破壞開始功能表等原處。Run 機碼免管理員、會出現在工作管理員「啟動應用程式」。
**Alternatives**: 一般檔案依拖入模式或一律複製；其他捷徑跟桌面一樣；工作排程；啟動資料夾。
**Consequences**: Run 值存在但工作管理員停用時視為關閉，我們切換時清掉該旗標。
**Author**: 使用者決定

---

### 2026-10-25 — 階段 6 慣例預設
**Decision**: 只有 widget 接受拖放（面板不接受）；原生 Win32 系統匣選單，左右鍵都開；設定視窗確定/取消、非模態、只能開一個；同名加「 (2)」；公用桌面捷徑沒權限移動就改複製；單一執行個體（`Local\` mutex）；自畫 App 圖示；拖放後對來源回報 Copy。
**Reason**: 慣例且簡單；回報 Move 會讓來源刪除它以為還在的檔案。
**Alternatives**: 面板也接受拖放；WPF ContextMenu；即時套用設定；允許多開。
**Consequences**: 原生選單在深色模式下為淺色。
**Author**: Claude（已於階段回報告知）

---

### 2026-10-25 — Shell 互通集中到 Shell/
**Decision**: `ShellImage`（原 Icons/）、`ShellItem`（原 Panel/）與新的 `ShortcutFile` 移到 `Shell/`。
**Reason**: 都是 Shell COM / ShellExecute 互通，且被多個模組使用（Icons、Panel、Folders、App）。
**Alternatives**: 留在各自使用的模組。
**Consequences**: namespace `DeskGrid.Shell`。
**Author**: Claude

---

### 2026-10-25 — XAML 不引用本組件型別
**Decision**: 對話框文字在 code-behind 設定，不用 `x:Static res:Strings.*`；`Strings` 維持 internal。
**Reason**: XAML 引用本地型別會觸發 WPF 暫時標記組件編譯，而 `Strings` 由 resx 在 build 時產生於 `obj/`，暫時組件編譯不到。
**Alternatives**: 把產生的 Strings.Designer.cs 放進原始碼（需手動同步）；自訂 build target 提早產生（會重複加入 Compile）。
**Consequences**: 每個視窗多一段 `LocalizeTexts()`。
**Author**: Claude

---

### 2026-10-25 — 發布：公開 repo、tag 自動 Release、MIT（Ryan Kuo）、雙語 README
**Decision**: 公開 repo `Ryan970714/DeskGrid`；push/PR 跑 CI（build、test、publish artifact）；推 `v*` tag 自動建 Release（附單一 exe 與 SHA256）；LICENSE 著作權人 Ryan Kuo；README 英文 + 繁中。
**Reason**: 使用者選定。
**Alternatives**: 私人 repo；只要 CI；只有英文 README。
**Consequences**: CLAUDE.md、context/ 也會公開（已掃描無個人資訊）；推送 workflow 檔需要 gh `workflow` 權限。
**Author**: 使用者決定

---

### 2026-10-25 — 打包設定
**Decision**: publish profile `win-x64`：self-contained + PublishSingleFile + IncludeNativeLibrariesForSelfExtract + 壓縮 + 無 pdb，輸出到 `artifacts/publish/`。CI 用 `dotnet-version: 10.0.x`（global.json 的 rollForward 仍適用）。README 不放截圖（避免露出個人桌面）。
**Reason**: 真正的單一 exe；壓縮後約 62 MB；WPF 不能 trim。
**Alternatives**: 框架相依（小但需安裝 .NET 10 Desktop Runtime）；不壓縮（約 150 MB，啟動略快）。
**Consequences**: 首次執行會把 WPF 原生 DLL 解到 %TEMP%。
**Author**: Claude

---

---

### 2026-10-25 — 專案更名 DeskGrid → DeskGrid
**Decision**: 專案、GitHub repo、solution/csproj/namespace、exe、app 資料路徑全部改為 `DeskGrid`（大小寫一致；log 檔 `deskgrid.log`）。
**Reason**: 使用者要求更名。
**Alternatives**: 只改 repo 與顯示名稱、保留內部 `DeskGrid` 識別碼（會造成名稱不一致）。
**Consequences**: 本檔與 `session-log.md` 之前的紀錄依「只能新增」規則保留舊名 `DeskGrid`，不回改。Config 改存 `%APPDATA%\DeskGrid\`、log 在 `%LOCALAPPDATA%\DeskGrid\`、預設根目錄 `%USERPROFILE%\DeskGrid\`、開機自啟登錄值名 `DeskGrid`、單一執行個體 mutex `Local\DeskGrid.SingleInstance`；舊安裝的資料夾需手動搬移。GitHub 舊網址會自動轉址。
**Author**: 使用者決定

---

### 2026-10-25 — Widget 格線改用桌面原生圖示格；工作列釘選用自建捷徑
**Decision**: (1) widget 格大小與錨點取自 Explorer 圖示 list view（跨行程讀 `LVM_GETITEMPOSITION`），並避開被原生圖示佔住的格。(2) 「釘選到工作列」不呼叫未公開的 pin API，而是建 `%APPDATA%\DeskGrid\Pins\<名稱>.lnk`（`DeskGrid.exe --open "名稱"`，圖示為預覽）並在檔案總管選取，請使用者自己釘；啟動時若已有執行個體，經具名管線轉交後結束。(3) 面板內拖曳：同面板重排、拖出由目標處理；拖到另一個 widget 一律移動。
**Reason**: 使用者回報 widget 與桌面圖示未對齊、互相重疊；Windows 11 不允許程式替使用者釘選（未公開 API 脆弱）。
**Alternatives**: 固定 76×96 格（舊，無法對齊）；`IPinnedList3` 未公開介面（隨 Windows 更新可能失效）；以 `WM_COPYDATA` 取代具名管線。
**Consequences**: 多螢幕下格大小以 Explorer 回報值為準（未驗證）；讀不到原生格時退回舊格線。使用者把圖示拖到 widget 上仍會重疊。
**Author**: Claude（使用者要求功能；實作方式待使用者驗證）

---

### 2026-10-25 — 面板：拖曳動畫（拖曳影子 + 滑動）、拖到 widget 自動開面板、標題就地改名
**Decision**: (1) 拖曳時用 `DragGhost`（不接輸入的置頂分層視窗）跟著游標，原格留空、其餘項目以 `TranslateTransform` 動畫滑開（iOS 式）；結束時影子飛回格子或縮小消失。拖離面板時缺口合起來。(2) 拖曳（面板項目或外部檔案）停在 widget 上 550 ms → `PanelController.OpenForDrag` 開那個資料夾的面板（不啟用）；原面板只隱藏（DWM cloak）到拖曳結束才關，因為 `DoDragDrop` 在它裡面執行。別的資料夾的項目放進開著的面板 → `ItemImporter.ImportTo` 搬進去，再 `WidgetCatalog.PlaceNewItem` 放到指定位置；放入前面板先在該格開缺口。(3) 面板標題：滑過顯示鉛筆、點擊變輸入框，Enter 確認、Esc 取消、失焦確認；`Directory.Move` 改名（只差大小寫時經暫存名稱），設定由既有的 Renamed 處理轉移。
**Reason**: 使用者要 iOS 式拖曳體驗與直接改名。
**Alternatives**: 改用自繪滑鼠拖曳取代 OLE（無法拖到桌面 / 別的程式）；直接關掉原面板（會中斷 OLE 拖曳迴圈）。
**Consequences**: 面板拖曳時需 Items 的 RenderTransform；放進滿列的面板時新的一列不會預先撐開視窗高度（已知限制）。未在實機驗證。
**Author**: Claude（使用者要求功能）

---

### 2026-10-25 — 版本檢查：以 GitHub release 為準，三選項彈窗，自動更新靠換檔腳本
**Decision**: 啟動後 8 秒與設定視窗按鈕查 `releases/latest`；較新時彈窗：更新（下載單檔 exe、驗 `.sha256`，由暫存的 PowerShell 腳本在本程序結束後覆蓋原 exe 並重啟）、維持（`skippedUpdateVersion` 記住該 tag，自動檢查不再提醒）、取消（不存，下次啟動再提醒）。開發版（非單檔）或安裝資料夾不可寫時，更新改為開 release 頁。
**Reason**: 使用者要求版本檢查與自動更新；執行中的 exe 無法自己覆蓋，所以需要外部腳本。
**Alternatives**: 內建更新程式 exe（多一個檔要發布）；不驗校驗碼（下載被竄改無從發現）；`WM_COPYDATA` 等。
**Consequences**: config 新增 `skippedUpdateVersion`（使用者同意）；未簽章 exe 的更新不受 SmartScreen 保護，靠 SHA256 與 GitHub HTTPS；真正的換檔流程要等有比 0.1.0 新的 release 才能實測。
**Author**: Claude（使用者要求功能）

---

### 2026-10-25 — 磁碟 / 登錄檔 / 網路失敗：記 log 繼續跑，不讓 app 崩潰
**Decision**: `ConfigStore.Save` 遇 `IOException` / `UnauthorizedAccessException`（同步軟體或防毒鎖檔、磁碟滿）只記 log，下次存檔會整份重寫；`AutoStart` 登錄檔存取被拒回傳 `false`；`UpdateChecker.ParseRelease` 對缺欄位 / 型別不符的 JSON 回傳 `null`；更新腳本以 UTF-8 BOM 寫出（Windows PowerShell 5.1 對無 BOM 檔用 ANSI，非 ASCII 使用者名稱會讓路徑亂碼）；log 超過 512 KB 輪替成 `.old`。
**Reason**: 全域未處理例外會結束程式（維持 fail-fast），所以可預期的環境失敗要在來源處接住，否則一次存檔失敗就讓桌面上的 widget 全部消失。
**Alternatives**: 把 `DispatcherUnhandledException` 設成 Handled（連真正的程式錯誤也吞掉，計時器例外會洗版）；`Save` 回傳成功與否（呼叫端無從補救）。
**Consequences**: 存檔失敗時使用者看不到提示，只在 log；設定可能在該次執行中途遺失。
**Author**: Claude（使用者要求找 bug 並修正）

---

### 2026-10-26 — 工作列「所有資料夾」：同一個面板視窗切換列表 / 資料夾；釘選捷徑各帶 AppUserModelID
**Decision**: 系統匣「把所有資料夾釘選到工作列…」建 `%APPDATA%\DeskGrid\Pins\All\DeskGrid 資料夾.lnk`（`DeskGrid.exe --all`，exe 圖示），管線訊息 `*`（不可能是資料夾名）。面板以列表模式開在游標處，資料夾格與桌面 widget 同樣的底板 + 2×2 預覽；點資料夾在**同一個視窗**換成該資料夾內容（啟動、重排、拖出、改名照舊），標題左側 ← 回列表。所有釘選捷徑寫入 `System.AppUserModel.ID`（`DeskGrid.Pin.All`、`DeskGrid.Pin.Folder.<名稱 SHA-256 前 8 bytes>`）。
**Reason**: 使用者要求工作列按鈕列出所有資料夾、之後邏輯同桌面資料夾。就地切換不必在拖曳 / 啟用之間開關兩個視窗；同一 exe 的捷徑沒有各自 AppUserModelID 時工作列會把它們當同一個 App 合併。
**Alternatives**: 點資料夾另開一個面板（要處理兩個面板與焦點）；工作列跳躍清單（點按鈕不會直接顯示）。
**Consequences**: 列表模式的資料夾格不接受拖放、不可拖動；舊版建立的資料夾釘選捷徑沒有 AppUserModelID，重建即可。
**Author**: Claude（使用者需求）

---

### 2026-10-26 — 面板拖出判斷用實體像素並在 QueryContinueDrag 補偵測；拖曳中重掃延後
**Decision**: 判斷游標是否離開面板改用 `GetWindowRect` + `GetCursorPos`（實體像素），並在每次 `QueryContinueDrag` 也檢查，不只依賴 `DragLeave`。拖曳進行中收到的重掃先存起來，放開後再套用；放回面板時落地中的項目即使被重建也維持隱藏直到影子到位。`DragGhost` 在 `SourceInitialized` 定位（顯示前），Lift / Land 共用一個計時器。
**Reason**: 使用者回報「拖出有時不收合」「拖曳影子有時顯示不正確」。DIP 換算會讓剛從右 / 下邊離開的游標被判在內，之後 `DragLeave` 不會再來；拖曳中重建項目會讓拖曳索引與原格錯亂；重排後新格立刻顯示造成雙圖示。
**Alternatives**: 拖曳中完全不更新面板（資料夾被刪時會顯示舊內容）。
**Consequences**: 第 2 項的原因是推測，未重現。
**Author**: Claude

---

### 2026-10-26 — Store App 捷徑的圖示：Apps 資料夾 + 不帶 icon-only 的後備
**Decision**: `.lnk` 沒有檔案路徑時，取其 ID list 的解析名稱，改對 `shell:AppsFolder\<名稱>` 要圖示；`ICONONLY` 得到通用文件圖示時，再以 `RESIZETOFIT` 要一次。兩者都失敗才用 ID list 項目本身，最後才用 `.lnk` 檔。
**Reason**: 對 Store App 的 `.lnk` 要圖示得到空白文件。WhatsApp 套件沒有 `ICONONLY` 要求尺寸的 logo，Shell 回通用圖示；不帶 `ICONONLY` 會找到 88 px 的 logo。實測在測試程序裡加載 WPF 後同樣的呼叫會失敗，而 DPI-unaware 的執行緒內容在測試有效、在 DeskGrid（PerMonitorV2）無效，所以不採用。
**Alternatives**: 讀套件 manifest 自己找 logo（需要 WinRT PackageManager，成本高）；DPI-unaware 執行緒（在本程式無效）。
**Consequences**: 這類圖示可能只有 88 px，預覽以 48 DIP 顯示，影響不大。
**Author**: Claude

---

### 2026-10-26 — 資料夾列表的順序存在 config `folderOrder`
**Decision**: `AppConfig.FolderOrder`（資料夾名稱清單）決定「所有資料夾」面板的順序；新資料夾排最後，消失的移除，改名時就地替換。拖曳資料夾只帶自訂格式、不帶 `FileDrop`。
**Reason**: 資料夾排序磁碟存不了，與項目排序（`ItemOrder`）同理放 config；不帶 `FileDrop` 以免放到桌面或別的 widget 時資料夾被搬走。
**Alternatives**: 依名稱排序（無法自訂）。
**Consequences**: `WidgetCatalog.Widgets` 依此排序，連帶影響沒存位置的新 widget 的擺放順序。
**Author**: 使用者決定（要求可拖曳排序），Claude 實作

---

### 2026-10-26 — 工作列按鈕再按一下收合：記下被工作列點擊關掉的面板
**Decision**: 面板關閉時若前景視窗是工作列（`Shell_TrayWnd` / `Shell_SecondaryTrayWnd`），`PanelController` 記下該面板（列表為 `*`、否則資料夾名稱）與時間；3 秒內收到同一面板的工作列請求（`ToggleAtCursor` / `ToggleListAtCursor`）就當作「收合」，不再打開。
**Reason**: 實測面板開著時再點工作列按鈕不會收合：按下時面板先失焦關閉，捷徑啟動的第二個 DeskGrid 程序隨後才把請求送來，此時已沒有開著的面板，於是又打開。
**Alternatives**: 只比時間（點工作列其他按鈕後 3 秒內再點 DeskGrid 按鈕會被吃掉，範圍更大）；比游標位置（使用者點完常立刻移開滑鼠，不可靠）。
**Consequences**: 先點工作列其他地方關掉面板、3 秒內再點同一個 DeskGrid 按鈕時需要多點一次。
**Author**: Claude

### 2026-10-27 — 拖曳影子改為純 Win32 分層視窗（自己註冊視窗類別）
**Decision**: `DragGhost` 不再是 WPF `Window` / `HwndSource`：以 `RegisterClassEx`（`DefWindowProc`）+ `CreateWindowEx` 建 `WS_EX_LAYERED` 視窗，圖示用 `RenderTargetBitmap` 畫進 DIB section，再 `UpdateLayeredWindow`；只在大小 / 透明度變動時重畫，移動只 `SetWindowPos`（每幀一次）。
**Reason**: 透明 WPF 視窗每次移動要等 WPF 出一幀（15–25 ms），拖曳時其他項目的滑動動畫會卡。改用 `HwndSource` 時 WPF 仍會畫進該視窗，`UpdateLayeredWindow` 一律失敗（87），影子顯示成黑色方塊。
**Alternatives**: 系統內建視窗類別（如 Static）免註冊，但意圖不明顯。
**Consequences**: 新增 `DragGhostTests` 檢查繪製沒有失敗（換回 `HwndSource` 時會失敗）。
**Author**: Claude

### 2026-10-28 — 桌面圖示落到 widget 上時由圖示讓開
**Decision**: DeskGrid 執行中，原生桌面圖示若蓋到 widget 格（Explorer 放新捷徑、使用者拖動），把該圖示移到最近的空格（`LVM_SETITEMPOSITION`）；桌面「自動排列圖示」開啟時圖示移不動，改由 widget 讓開。偵測用 WinEvent（只掛桌面執行緒：list view 的新增 / 刪除事件 + `EVENT_SYSTEM_CAPTUREEND`），300 ms debounce，不輪詢。
**Reason**: 使用者選定（widget 是使用者自己排好的位置，不該被新捷徑擠走）。實測 list view 移動項目不發 WinEvent，所以用拖曳結束時的 capture end 補上。
**Alternatives**: widget 讓開（不動使用者的圖示，但 widget 會被擠走）；輪詢圖示位置（簡單但持續耗用跨行程呼叫）；`IFolderView::SelectAndPositionItems`（官方 API，需透過 IShellWindows 取桌面 view，程式量較大）。
**Consequences**: 桌面上每次點擊 / 拖曳結束都會重讀一次圖示格（跨行程，每個圖示 4 次 SendMessage）。啟動時既有的重疊仍由 widget 避開（沿用原擺放規則）。被移動的圖示位置是否在 Explorer 重啟後保留未驗證。
**Author**: Claude

### 2026-10-28 — 面板改尺寸時先移位再放大
**Decision**: 面板要換大小時（換內容、拖入時長高動畫），先以實體像素一次 `SetWindowPos` 設好新位置與新大小，再設 WPF `Width/Height`；不再「先改 WPF 尺寸、再 `SWP_NOSIZE` 移位」。
**Reason**: 面板貼著螢幕底部時，WPF 先往下長、超出螢幕，再被移上來；超出螢幕那段之後不會重畫，最後一列名稱看起來被切掉（UIA 量到的版面是對的）。
**Alternatives**: 移位後強制整窗重繪（治標，仍可能閃一下）。
**Consequences**: `Reposition(window, size)` 可同時設大小；`ShowItems` 結尾的單獨 `Reposition` 移除（`FitToContent` 已處理）。
**Author**: Claude

### 2026-10-28 — 截圖工具拿走前景時面板不收合
**Decision**: 面板失去啟用時，若前景視窗屬於 Windows 截圖工具（`SnippingTool`，Win10 的 `ScreenClippingHost`）就不收合；以 `EVENT_SYSTEM_FOREGROUND`（out-of-context）等前景離開截圖工具：回到面板 → 照常；到其他視窗 → 收合。
**Reason**: 使用者截圖回報問題時面板會先消失。實測 Win+Shift+S 覆蓋層是 `SnippingTool.exe` 的 `XamlWindow`，取得前景。
**Alternatives**: 改用滑鼠 hook 判斷「點外部」而不靠失焦（改動大，鍵盤切換等情況也要另外處理）；截圖結束後強制把面板拉回前景（跨行程搶前景不可靠）。
**Consequences**: 只認行程名稱，第三方截圖工具（ShareX、Snipaste 等）仍會讓面板收合。
**Author**: Claude

---

### 2026-10-29 — 刪除資料夾：進資源回收筒，項目預設先移到桌面
**Decision**: widget 右鍵「刪除資料夾…」，確認後資料夾一律以 `SHFileOperation(FOF_ALLOWUNDO)` 送資源回收筒。設定 `deleteMode`：`moveToDesktop`（預設）先把資料夾內可見的檔案與子資料夾移到使用者桌面（同名加「 (2)」），任何一個移不過去就**不刪資料夾**並顯示原因；`deleteItems` 連項目一起進資源回收筒。
**Reason**: 使用者要求兩種處理方式可選（一併刪除 / 移至桌面）。預設選保留資料的那個；資源回收筒讓誤刪可還原，所以不另外做「永久刪除」。
**Alternatives**: 直接 `Directory.Delete`（不可還原）；`Microsoft.VisualBasic.FileIO`（多帶一個組件，且專案規定 Win32 走 CsWin32）；`IFileOperation`（較新但設定較多）。
**Consequences**: 移到桌面的項目由 Explorer 自己擺位置，可能落在 widget 格上，交給既有的 `ClearIconsOffWidgets`；工作列釘選的暫存捷徑不清理（見 todo）。
**Author**: 使用者決定要有此設定，預設值與資源回收筒由 Claude 選擇

---

### 2026-10-29 — 桌面圖示疊圖示：單一透明接收區跟著游標
**Decision**: 使用者選「桌面圖示疊圖示 → 兩個一起進新資料夾」。實作：`DesktopIconWatcher` 加 `EVENT_SYSTEM_CAPTURESTART` 偵測按下；按在圖示上且位移超過拖曳距離才算拖曳；拖曳期間只用**一個** alpha=1 的透明 `DesktopSurface`（`AllowDrop`），每 20 ms 移到游標所在圖示的範圍上，放開後 500 ms 收掉。圖示名稱用 `LVM_GETITEMTEXTW` 讀、`DesktopFiles` 對應到桌面檔案；對應不到 / 名稱重複 / 桌面上的真資料夾不接受。
**Reason**: Explorer 把「放到另一個捷徑上」當「用它開啟」，沒辦法在放開後補救，只能在拖曳期間接手。每個圖示一個視窗實測 26 ms × 40 = 1 秒會凍結拖曳開始；alpha=0 的視窗穿透（實測），alpha=1 看不見但會被命中。
**Alternatives**: 每個圖示一個接收區（太慢，可預建池但更複雜）；全螢幕單一視窗 + 區域（`SetWindowRgn`，圖層視窗行為不確定）；只做「把兩個 widget 疊起來合併」（風險低但不是使用者要的「app 疊 app」）；`IFolderView` 取圖示路徑（精確但跨行程 COM 風險高，改用名稱對應）。
**Consequences**: 拖曳期間「把檔案拖到程式捷徑上用它開啟」在游標落在這些圖示時被改成建資料夾；游標移進圖示到接收區就位約 20 ms，極快的拖放可能漏接；所有 Explorer 讀取都在按下 / 開始拖曳時做，不在 OLE 回呼裡。桌面行為無法單元測試（見 CLAUDE.md 階段 8 的測試注意）。
**Author**: 使用者選定做法，Claude 實作

---

### 2026-10-29 — 移除桌面上的「新增資料夾」磚
**Decision**: 桌面不再顯示「新增資料夾」磚（`WidgetBoard` 的 add tile、`FolderWidget.ShowAddTile`、`WidgetAddTile` 字串、config `addTilePosition` 全部刪除）。新增資料夾剩系統匣「新增資料夾…」、桌面圖示疊圖示、在根目錄建子資料夾。
**Reason**: 使用者要求。疊圖示上線後，磚的「拖檔案進去建資料夾」功能被取代，磚本身也佔一格桌面。
**Alternatives**: 保留磚但可在設定隱藏（使用者沒要）。
**Consequences**: 一次拖多個檔案建資料夾的路徑只剩「疊圖示」（一次兩個以上：多選後拖到另一個圖示上）；非桌面來源的檔案（例如開始功能表）要先建資料夾再拖進去。舊 config 的 `addTilePosition` 被 JSON 反序列化忽略，下次存檔即消失。
**Author**: 使用者決定，Claude 實作

---

### 2026-10-30 — 開機自啟流程、面板原生右鍵選單、刪除 / 新增資料夾的入口
**Decision**:
1. 開機自啟：Run 機碼命令加 `--autostart`；帶這個旗標的啟動先等桌面圖示排好（連兩次讀到相同位置，最多 45 秒）才建 widget；第一次執行問一次要不要開機啟動（config `autoStartAsked`）。使用者自己啟動不等。
2. 面板裡的檔案項目右鍵 = Explorer 的原生選單（`IContextMenu`，傳統版）；不含「重新命名」。
3. 資料夾的入口：「新增資料夾…」加到 widget 右鍵與面板名稱列右鍵；「刪除資料夾…」加到面板名稱列與「所有資料夾」列表的資料夾格右鍵（原本只有 widget 右鍵）。**桌面「新增資料夾」磚不恢復**（2026-10-29 使用者要求移除，仍有效）。
4. 應用程式控制（Smart App Control）封鎖未簽章 exe：程式碼無解，只在 README 寫解法；真正的解法是程式碼簽章（見 todo）。
**Reason**:
1. 登入時桌面 list view 已存在但圖示還沒排好，格線沒有錨點，widget 被吸到退回格線並寫回 config → 位置被蓋掉。不能一律延遲（手動啟動會多等），所以用旗標區分。第一次詢問比預設開啟或完全沉默更不擾民，也讓使用者知道有這個功能。
2. 使用者要求「與桌面原圖示一樣的 Win 內建選單」。傳統選單有公開 API；Win11 精簡選單沒有。重新命名需要 Explorer 的 view，這裡沒有。
3. 使用者回報「新增資料夾不見了」「不進根目錄就能刪資料夾」；原意不確定（可能是已移除的磚，或 0.3.0 的入口不夠明顯），所以在使用者會看的地方都放入口，而不是恢復磚。
4. SmartScreen 有「仍要執行」，Smart App Control 沒有；沒有受信任的簽章就過不去。
**Alternatives**: 一律延遲啟動（手動啟動也等，否決）；預設開機自啟不問（否決）；恢復桌面磚（使用者已要求移除）；用 Win11 精簡選單（沒有公開 API）；自簽憑證（SAC 不認）。
**Consequences**: 舊的 Run 項目在下次啟動時由 `UpdatePath` 補上 `--autostart`。面板在原生選單的指令執行期間不因失焦收合。未簽章 exe 在 SAC 電腦上仍會被擋。
**Author**: Claude（使用者列的需求，細節由 Claude 決定；第 3 點的原意待使用者確認）

---

### 2026-10-30 — 把資料夾拖到資源回收筒 = 刪除資料夾
**Decision**: widget 拖曳放開時，若游標在桌面的資源回收筒圖示上，widget 回到原格，並走原本的「刪除資料夾…」確認流程（含 `deleteMode`）。不直接刪、不另做確認介面。
**Reason**: 使用者回報「資料夾拖到垃圾桶無法被刪除」。桌面上資源回收筒是 Explorer 的圖示，widget 以前只會吸到最近空格。保留確認是因為拖曳很容易誤放，而且 `deleteMode` 可能把項目一併刪除。
**Alternatives**: 拖曳中即時高亮資源回收筒（Explorer 的圖示不能改，需另畫覆蓋層，沒做）；放開就直接刪（太危險）。
**Consequences**: 靠圖示下的名稱辨識資源回收筒（`RecycleBin.DisplayName`），改名或換語言都會跟著走；桌面上沒顯示資源回收筒時不會有這個行為。
**Author**: 使用者要求，Claude 決定細節

---

### 2026-10-30 — 設定視窗收納：開啟根目錄、釘選工作列、GitHub、更新紀錄
**Decision**: 系統匣選單只留「新增資料夾…」「設定…」「開機時啟動」「結束」。「開啟根目錄」改成設定視窗根目錄欄位旁的「開啟」按鈕；「把所有資料夾釘選到工作列…」改成設定視窗「工作列」卡片的按鈕；設定視窗新增 GitHub 網址（可點）與「更新紀錄…」。更新紀錄的內容是把 `CHANGELOG.md` 編進 exe（EmbeddedResource），在 `ChangeLogWindow` 顯示。
**Reason**: 使用者要求。系統匣選單太長；根目錄與釘選是設定性質的動作。更新紀錄編進 exe：離線可看、永遠對應執行中的版本（不必連 GitHub），且單檔 exe 不需另外帶檔案。
**Alternatives**: 從 GitHub 抓 release notes（要網路、版本不一定對應）；另寫一份中文更新紀錄（每次發版要多維護一份，沒做，更新紀錄目前只有英文）。
**Consequences**: 「開啟」用輸入框裡的路徑（可能尚未儲存），不存在就提示；設定視窗變高，改放進 `ScrollViewer`，螢幕矮時捲動。`CHANGELOG.md` 的格式要維持 `##` / `###` / `- `，否則視窗不會顯示該行（`ChangeLogTests` 會檢查最新一筆是 Unreleased 或目前版本）。
**Author**: 使用者要求，Claude 決定細節

---

### 2026-10-30 — 為 SignPath Foundation 免費程式碼簽章做準備
**Decision**: repo 端先備妥條款要求的東西，但**不代使用者申請**：(1) 設定新增「DeskGrid 啟動時檢查更新」（config `checkForUpdatesAtStartup`，預設開），關掉後除非使用者手動按才連 GitHub；(2) README（en / zh-TW）新增「Privacy / 隱私」（列出所有網路行為與開機自啟的登錄項）與「Code signing policy / 程式碼簽章政策」（角色、簽章範圍、流程）；(3) csproj `IncludeSourceRevisionInInformationalVersion=false`，產品版本不再帶 `+<commit>`，`AssemblyMetadataTests` 守著產品名稱與版本；(4) 簽章政策目前寫「尚未簽章、打算申請」，不提前宣稱已由 SignPath 簽章。
**Reason**: 免費簽章的條款（<https://signpath.org/terms>，草稿）要求：連網到外部系統要在隱私政策揭露並提供關閉選項；產品名稱 / 版本固定且被強制檢查；首頁與 Release 頁要有 Code signing policy 與角色名單。DeskGrid 啟動 8 秒後會自動連 GitHub，原本沒有關閉選項，產品版本也帶 commit hash（實測 `0.3.2+007e797…`）。簽章聲明在核准前寫成已簽章會是不實的。申請表單與帳號涉及使用者本人身分，由使用者自己填。
**Alternatives**: 預設關閉自動檢查（更保守，但使用者不會自動得知新版，沒採用）；首次執行時詢問是否自動檢查（多一個對話框，條款沒要求，沒做）；現在就把簽章聲明寫進 README（核准前不實，否決）；現在就改 `release.yml` 接 SignPath（沒有 organization id / token，無法測，核准後再做）。
**Consequences**: 一人兼任作者 / 審查者 / 核准者是否被接受，要等審核結果；若被要求第二人審查，要另想辦法。核准後要把 README 的簽章聲明改成生效，並在 Release 說明放 Code signing policy 連結。新增任何網路行為都要同步改 README 的隱私段落。SignPath 憑證能否讓 Smart App Control 放行，沒有來源明確保證，簽章後要實測。
**Author**: 使用者要求「盡可能修復以符合要求」，Claude 決定細節

---

### 2026-10-31 — 拖曳時自動開 / 關資料夾的規則（仿 iOS）
**Decision**: 拖曳停在 widget 上 550 ms 打開的面板，跟著那次拖曳走：游標離開面板與 widget 超過 300 ms → 關；拖曳結束（左右鍵都放開 250 ms）沒有東西放進來 → 關；有放進來 → 留著並嘗試取得焦點，拿不到就等使用者在面板與 widget 以外按下滑鼠才關。從面板拖出的項目同樣用「離開面板與 widget 300 ms」判斷拖出；拖回原資料夾的 widget 停 550 ms → 原面板重新出現可放回。widget 整格（alpha=1 底色）可點、可放。
**Reason**: 使用者要求修正「拖動 logo 以及自動開啟 / 關閉資料夾的邏輯」。原本拖曳開的面板以不啟用方式顯示，永遠收不到 Deactivated，拖走或放到別處都不會關、點外面也不關；拖出的項目回不去原資料夾；widget 只有圖片 / 文字像素可命中，放在旁邊會落到桌面（實機 log 10-31 有一筆被推開的圖示）。
**Alternatives**: 拖出立即收（原行為，從面板往 widget 移動會經過空隙而關掉）；拖曳開的面板一律放下後就關（看不到剛放進去的項目，沒採用）；改用全域滑鼠 hook 偵測點外面（過重，沒採用，用 50 ms 計時器看按鍵狀態）。
**Consequences**: 每個與拖曳有關的面板在拖曳期間多一個 50 ms 計時器；拿不到焦點的面板 Esc 無效。widget 整格會擋住格內的桌面點擊（格子本來就保留給 widget）。
**Author**: 使用者要求，Claude 決定細節（未另行詢問）

---

### 2026-10-31 — 面板固定在開啟的那一側
**Decision**: 面板的方向（下 / 上 / 左 / 右）只在第一次定位、或換顯示別的資料夾 / 列表時決定；之後內容變大只在同一側夾回工作區，可能蓋住 widget 一部分。
**Reason**: 使用者回報拖入時面板瘋狂閃爍；log 證實是「長高一列 → 下方放不下 → 跳到右側 → 游標不在面板 → 收回 → 回到下方」的循環。
**Alternatives**: 拖入時不長高（空位要捲動才看得到）；跳邊後不因 DragLeave 收回（面板會離開游標，放不進去）。
**Consequences**: 螢幕下方的 widget 開的面板長高時會往上移、蓋住 widget 下半部，不再跳到另一側。
**Author**: Claude（依使用者回報）

---

### 2026-10-31 — 開啟面板的過場動畫，不加設定開關
**Decision**: 面板出現時內容從 widget 中心放大（0.85 → 1）並淡入，200 ms ease-out；跟隨 Windows「動畫效果」設定，關閉時不播。DeskGrid 設定裡不另加開關。
**Reason**: 使用者要求加動畫，並說「如果會佔用太多效能才在設定加開關（預設開）」。動畫只改一個元素的 RenderTransform / Opacity，由 WPF 合成處理，200 ms 內成本可忽略，不符合加開關的條件；跟隨系統設定，不想要動畫的人仍可關掉。
**Alternatives**: 整個視窗（含 Acrylic 背景）從小放大（每幀 SetWindowPos + DWM 重畫背景，較耗資源；DWM 自己的轉場在 Acrylic 上看起來會頓，早已關閉）；加設定開關（YAGNI，使用者要求時再加）。
**Consequences**: Acrylic 樣式是背景先出現、內容再長出來；收合沒有動畫。
**Author**: 使用者要求，Claude 決定細節

---

### 2026-10-31 — 開啟動畫改成「資料夾從 widget 展開」，並加設定開關（取代同日前一筆的做法）
**Decision**: 動畫改由另一個點穿的置頂透明視窗（`PanelOpening`）播放：底色方塊從 widget 的圖示方塊長到面板大小，面板內容（`VisualBrush` 即時畫）跟著縮放淡入；長完才顯示真正的面板，動畫視窗淡出。設定新增「開啟資料夾時播放動畫」（config `animatePanelOpening`，預設開）；Windows「動畫效果」關閉時也不播。
**Reason**: 使用者實看前一版後說只是「logo 稍微變大」，要「從縮小狀態的資料夾展開成完整畫面」。面板視窗在 widget 旁邊，只縮放面板內容畫不到 widget 的位置，必須有一個蓋住兩者的視窗。layered 視窗每幀重畫比前一版耗資源，符合使用者說的「佔用效能就加開關、預設開」。
**Alternatives**: 讓面板視窗本身從 widget 大小 SetWindowPos 長大（WPF 每幀重新排版、Acrylic 重畫，內容會被裁切，且容易黑邊閃爍）；拍面板內容的靜態截圖（圖示非同步載入，截圖可能缺圖示）。
**Consequences**: 系統毛玻璃背景在動畫期間以不透明底色代替，最後淡出時換成真的毛玻璃；面板晚約 0.3 秒可以點 / 接受放下（拖曳停住開的也是）；收合沒有動畫。
**Author**: 使用者要求，Claude 決定細節

---

### 2026-10-31 — 取消開啟資料夾的過場動畫（撤銷同日前兩筆）
**Decision**: 兩版開啟動畫（只縮放內容；`PanelOpening` 疊加視窗從 widget 展開）全部移除，程式回到動畫前（be599b8）的狀態，設定的「開啟資料夾時播放動畫」與 config `animatePanelOpening` 一併移除。面板內原有的動畫（拖曳時項目滑動、長高、拖曳影子）不受影響。
**Reason**: 使用者實看後說「效果太糟糕」，要求取消所有過場動畫。
**Alternatives**: 調整速度 / 曲線再試（使用者明確要取消）。
**Consequences**: 舊 config 若有 `animatePanelOpening` 會被忽略。之後若要再做開啟動畫，要先跟使用者確認想要的效果，並注意：Acrylic 背景無法跟著縮放、面板視窗畫不到 widget 的位置。
**Author**: 使用者要求
