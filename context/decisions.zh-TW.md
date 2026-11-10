# Decisions

> 只能新增，不得覆蓋或刪除既有紀錄。若某個決策後來被推翻，新增一筆新紀錄說明並引用原紀錄的日期，不要修改原紀錄。
>
> 本檔與 `decisions.md`（英文）互為雙語版：每一筆新紀錄兩份都要加。見 `AGENTS.md`「語言政策」。

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


---

### 2026-11-01 — 多螢幕：widget 位置以設定檔為準，surface 漂移時搬回去
**Decision**: (1) `DesktopSurface.Resync()` 比對視窗實際螢幕位置與 `Position`，不同就搬回；`DesktopHost` 每秒檢查並在 `WM_DISPLAYCHANGE` 立即執行，1.5 秒後發 `DisplayChanged`，`WidgetBoard.OnDisplayChanged` 依設定檔存的位置重擺。(2) 存檔位置不在任何螢幕上（螢幕被拔）→ 暫放最近螢幕、不覆蓋存檔，螢幕回來自動回去。(3) 面板 `Reposition` 一律帶 DIP 大小並處理 `WM_DPICHANGED`，混合 DPI 時也貼在 widget 旁。
**Reason**: 使用者回報接外接螢幕並放在主螢幕左邊後，所有 widget 跑到左邊、在左邊開面板卻出現在主螢幕。原因：surface 是涵蓋所有螢幕的 Progman / DefView 的子視窗，在主螢幕左 / 上加螢幕時父視窗原點變負，子視窗 client 座標不變 → 螢幕上整批漂移；`Position` 與設定檔沒變，面板以 `Position` 定位所以開在主螢幕。實機用 `ChangeDisplaySettingsEx` 切換第二螢幕左右重現並驗證。
**Alternatives**: 設定檔改存「螢幕 + 相對位移」（螢幕識別碼不穩：`\\.\DISPLAYn` 會重編、EDID id 同型號會撞，且主螢幕不變時螢幕座標本身穩定，未採用）；只在 `WM_DISPLAYCHANGE` 時搬（Explorer 調整父視窗的時機不固定，且漏訊息就永遠偏移，所以再加每秒檢查，成本是每秒對每個 widget 一次 `GetWindowRect`）。
**Consequences**: 主螢幕被換成別台時（座標原點變了），存檔座標會對到不同的實體位置，這情況尚未處理。顯示設定改變時開著的面板會被關掉。
**Author**: 使用者要求徹底修復，實作方式由我決定


---

### 2026-11-02 — 掃描對單一捷徑 / 網路目標的穩健性
**Decision**: (1) `ShortcutFile.TryGetTarget` 的 `Load` 把 `IOException` / `UnauthorizedAccessException` 也當「讀不到」回 null（marshaller 把 `0x80070002` 轉成 `FileNotFoundException`）。(2) `FolderScanner` 以 `.lnk` 修改時間快取捷徑指向的路徑，每次掃描只重讀目標的時間戳；目標在網路（UNC 或網路磁碟機）時不讀時間戳（視為 `default`）。(3) `ConfigStore.Open` 讀檔遇 `IOException` / `UnauthorizedAccessException` 重試 4 次（間隔 250 ms），仍失敗才丟出，不退回預設值。(4) `InstanceChannel` 監聽迴圈捕捉所有例外後繼續；`App` 記錄 `AppDomain` / `TaskScheduler` 未處理例外。
**Reason**: (1) 掃描中 `.lnk` 被刪或被別的程式鎖住時，例外被 `Scan` 的逐資料夾 catch 接住 → 整個 widget 不在結果裡 → `Reconcile` 當它消失，清掉位置與排序（已寫測試重現）。(2) 掃描在 UI 執行緒，斷線的網路共用讀時間戳會卡到連線逾時，每次重掃一次。(3) 登入時病毒掃描 / 同步程式短暫鎖住 config 會讓啟動直接當掉；退回預設值則會把真正的設定覆蓋掉。(4) 沒有人 await 監聽任務，任何例外都會讓工作列按鈕無聲失效。
**Alternatives**: 單一資料夾讀失敗時保留舊的掃描結果（較大改動，捷徑已涵蓋常見成因）；網路目標改在背景讀時間戳（複雜度高，換到的只是網路捷徑的圖示在目標更新時自動重畫）。
**Consequences**: 指向網路位置的捷徑在目標更新後圖示不會自動重畫（要重啟）。單一資料夾因其他原因讀失敗仍會暫時從掃描消失。
**Author**: 使用者要求（/goal），實作方式由我決定

---

### 2026-11-02（二）— 執行中的未處理例外只記 log，不結束程式
**Decision**: `App.OnUnhandledException` 在啟動完成後（`_started`）把 `DispatcherUnhandledException` 標為 `Handled`（`OutOfMemoryException` 除外）；啟動途中仍照舊結束。同時：`MonitorArea.Of` 取不到 DPI 時退回 96（不再丟例外）、`ScreenCapture.IsTool` 也接 `InvalidOperationException` / `Win32Exception`、`DoDragDrop` 失敗時把拖曳影子收掉、`ShellContextMenu` 接 `UnauthorizedAccessException`、空資料夾刪不掉只記 log、開機等桌面排好時讀取失敗改為照常往下（以前 `StartWidgetsAsync` 會無聲失敗、widget 永遠不出現）。
**Reason**: 常駐程式的事件處理（拖曳、選單指令、計時器）出一次錯就整個結束，桌面上所有 widget 會一起消失。
**Alternatives**: 維持結束（避免狀態不一致，但使用者看到的是全部消失）；每種例外各自 try/catch（漏一個就當掉）。
**Consequences**: 同一個例外若每次計時器都發生，會一直寫 log（log 超過 512 KB 會輪替）。
**Author**: 使用者要求（/goal），實作方式由我決定

---

### 2026-11-03 — 輕量化：widget 軟體繪圖、exe 只帶用得到的語系
**Decision**: `DesktopSurface` 與 `ShellMessageWindow` 的 `HwndSource` 設 `RenderMode.SoftwareOnly`；csproj 加 `SatelliteResourceLanguages=en;zh-Hant;zh-TW`。面板（`PopupPanel`）維持硬體繪圖。
**Reason**: 使用者要求在功能完全一致下輕量化。實測閒置 private 117 → 83 MB（managed heap 只有 4 MB，其餘是 WPF 的 D3D 裝置）；exe 65.1 → 61.9 MB。widget 截圖與硬體繪圖逐像素比對只差邊緣抗鋸齒（≤ 15/255）。
**Alternatives**: 整個程序 `ProcessRenderMode = SoftwareOnly`（同樣 83 MB，但面板 Acrylic 依賴 DWM 延伸框架，軟體繪圖可能變黑，且使用者在用電腦無法充分目視驗證）；把圖示快取縮成顯示尺寸（約省 13 MB，但高 DPI 畫質可能變）；改 framework-dependent 發布（exe 小很多但要使用者裝 runtime，改變發布方式）。程式碼本身 analyzer 掃過幾乎沒有死碼，不做大重構。
**Consequences**: 開過面板後 D3D 裝置仍會建立（實測約 146 MB，之後回落）。日文等其他語系的 Windows 上，WPF 內建文字（文字框右鍵）改顯示英文——DeskGrid 本身也只有 en / zh-TW。
**Author**: Claude（依使用者 /goal）

---

### 2026-11-03（二）— 處理 GitHub issues：可程式化的先做，其餘列出原因
**Decision**: 依 issue 內容實作 #9（拿不到焦點的拖曳面板用 Esc 關）、#12（已刪資料夾的釘選按鈕 → 開列表）、#13（面板位置取點擊當下的游標，`OpenMessage` 隨管線送出）、#16（`CHANGELOG.zh-TW.md` + `ChangeLogResource` 字串）、#18（`MenuTheme`，未公開 uxtheme ordinal）、#20（`PanelNavigation` 方向鍵）、#21（`WM_SETTINGCHANGE` → 與顯示變更共用同一條重新吸附流程）、#22（拖曳中 Esc，輪詢 `GetAsyncKeyState`）、#24（根目錄回復 watcher）、#25（`ShortcutTargetWatcher`）。沒做的：#19（Start 功能表拖曳的資料格式沒實測過，盲寫風險高）、#1–#7（要實機）、#8（要使用者填 SignPath 申請）、#10 / #23 / #26 / #11（需求待使用者決定）、#14（研究：無公開 API）、#15（觀察中，連跑 5 次 239 測試全過）、#17（要乾淨桌面截圖）。
**Reason**: AGENTS.md：需求不清楚要問、實機做不到的不宣稱已驗證。
**Alternatives**: #18 不碰未公開 API，只改文件說明（但 issue 就是要選單變深色）；#25 用定時重掃（簡單，但每分鐘多一次全掃，且不即時）；#22 用低階鍵盤 hook（需要全域 hook，比 30 ms 輪詢重）；#13 固定開在工作列上方（要猜工作列位置與方向）。
**Consequences**: 所有新行為只有單元測試，拖曳 / 工作列 / 深色選單 / 工作列變更都沒實機驗證。`MenuTheme` 依賴 uxtheme ordinal 135 / 136，Windows 更新可能拿掉，測試會先警告。
**Author**: 使用者要求「完成所有 issue」，實作方式由我決定

---

### 2026-11-03（三）— 使用者回答 #10 / #23 / #26 / #11 的決策題
**Decision**: #10：就是桌面「新增資料夾」磚被移除，不是 bug，關閉 issue。#23：「顯示桌面圖示」關閉時 widget 跟著隱藏（`DesktopHost` 每秒看 list view 可見性）。#26：框選時一併選取 widget 並可一起拖動（`MarqueeSelector`），Ctrl+A / Ctrl 加選不做。#11：做外部檔案拖到「所有資料夾」列表的資料夾格。
**Reason**: 使用者在 AskUserQuestion 中選定。#23 先實測確認 Explorer 只隱藏 `SysListView32`（原本 widget 會留著）。
**Alternatives**: #23 保持顯示（不改）；#26 偵測選取用 Explorer 的 list view 選取狀態（要跨行程讀 `LVM_GETITEMSTATE`，仍畫不出 widget 的選取框）、只做視覺不做拖動；#11 關閉 issue。
**Consequences**: 框選與一起拖動依賴 `EVENT_SYSTEM_CAPTURESTART` 與按下點／游標推算框，Explorer 的框選實際判定（圖示圖片 + 名稱區）以內縮 15% / 10% 近似；全部沒有實機驗證。
**Author**: 使用者決定，實作方式由我決定

---

### 2026-11-03（四）— #27 面板緊貼工作列、#19 開始功能表 App 拖入
**Decision**: #27：`PanelPlacement` 在算邊與位置前先把錨點夾進工作區（`Within`）。#19：不改 `ItemImporter`，把 `Shell IDList Array` 的虛擬項目先做成暫存 `.lnk` 再當一般拖入的捷徑處理（`DroppedItems`）。
**Reason**: #27 的截圖看不到，依描述（開面板後與工作列有距離、偶發）推測錨點在工作列上、離工作區邊緣的距離隨游標位置變動。#19 讓三個放開處與 `ItemImporter` 的規則（捷徑複製、同名編號）不必各自認識 PIDL。
**Alternatives**: #27 固定開在工作列上方（要猜工作列方向）；#19 讓 `ItemImporter` 直接收 PIDL（要改 `Plan` / 三個呼叫端 / 事件參數的型別）、或對虛擬項目用 `shell:AppsFolder\AUMID` 字串建 `.lnk`（要自己取 AUMID，且 `SetIDList` 更直接）。
**Consequences**: 暫存 `.lnk` 在放開後即刪；程序在放開途中被殺會留下 `%TEMP%\DeskGrid\drops\` 的空殼，沒有啟動時清理。#19 的資料格式假設沒實測。
**Author**: 使用者要求，實作方式由我決定

---

### 2026-11-03（五）— 雙語 App 與文件：可選語言、`NAME.md` + `NAME.zh-TW.md`、置中展開面板
**Decision**:
1. **App 語言**：新增設定「語言」（config `language`：`system` / `english` / `traditionalChinese`；預設 `system`＝Windows 為 zh-TW / zh-HK / zh-MO / zh-Hant 時用繁體中文，否則英文）。`UiLanguage.Apply` 在啟動與套用設定時切換整個程序（`DefaultThreadCurrentUICulture`、`CurrentUICulture`、`Strings.Culture`）；文字在建立視窗 / 選單 / 對話框時才取，所以不必重新啟動。更新紀錄與隱私連結跟著語言（本來就有各語言的資源）。
2. **文件**：所有進版控的 Markdown 都有 `NAME.md`（英文）與 `NAME.zh-TW.md`（繁體中文）兩個版本，結構相同。`CLAUDE`、`AGENTS`、`context/decisions`、`context/project-summary` 新增英文版（原本的中文檔用 `git mv` 改名為 `*.zh-TW.md`，保留歷史）；`README` 與 `CHANGELOG` 本來就是這樣。政策寫在 `AGENTS.md`（「語言政策」）與 `CLAUDE.md`，每位開發者的 AI 助理都會先讀到。`UiLanguageTests`（resx 的 key 與佔位符一致）與 `MarkdownPairsTests`（雙語版存在、標題結構相同）把關。本機、已 gitignore 的工作筆記與 `LICENSE` 不在範圍內。
3. **面板展開位置**：新增設定「資料夾展開方式」（config `panelOpenMode`）：*附近展開*（原本的行為，預設）或*置中展開*：開在 widget 所在螢幕工作區的正中央，無論資料夾裡有多少東西都是同樣大小——寬 `columns` 欄、高 `columns` 列（只有螢幕太矮時才減列），項目在裡面捲動，捲軸的位置永遠預留。拖曳停在 widget 上而開啟的面板一律附近展開。
**Reason**: 使用者要求整個 App 與所有文件都能選擇雙語（繁體中文 / 英文），並把政策寫進對應文件，讓他或另一位開發者的 AI 助理之後繼續沿用；以及兩種展開資料夾的方式，第二種是為了不讓不同資料夾因為 App 數量不同而開在不同位置、不同大小（大小取自設定的欄數）。
**Alternatives**: 文件：repo 內只留英文（否決：擁有者與部分協作者用中文）；中文檔沿用 `CLAUDE.md` 這個原名（否決：`README` / `CHANGELOG` 已經是英文用原名，而且英文的 `CLAUDE.md` 才是任何協作者的助理預設會載入的）；一個檔案放兩種語言（檔案很長、diff 雜亂、無法依語言編進 exe）。語言：只能重新啟動才切換（文字本來就在建立時才取，多寫程式沒好處）；用自己的對話框取代 `MessageBox`，讓按鈕跟著設定（為了幾個按鈕太大）。置中展開：選正方形（列數＝欄數）作為「由欄數決定」的大小——使用者沒指定列數；固定寬度、高度隨內容的做法否決，因為那正是要避免的不一致。
**Consequences**: 之後每次改文件都是同一個 commit 內改兩份（測試檢查得出結構，檢查不出譯文本身）。Windows 自己的部分（`MessageBox` 按鈕、檔案總管右鍵選單）跟著 Windows 的語言而不是設定。列數＝欄數的置中面板在 8 欄時很高，會被裁到螢幕放得下。`project-summary` 仍保留舊的「Current Status」文字（照原樣翻譯）。本筆取代 2026-10-24「文件語言」那一筆。沒有實機驗證：執行中切換語言、置中展開面板在真實桌面上的樣子。
**Author**: 使用者要求；細節由 Claude 決定（正方形的大小與「拖曳開啟的面板一律附近展開」使用者沒有指定）

---

### 2026-11-06 — 找 bug：開著的 issue 與全部程式碼重讀
**Decision**: 開著的 issue（#1–#7 實機驗證、#8 SignPath 申請、#17 README 截圖）都需要使用者或真實桌面，沒有關閉任何一個。重讀 `src/DeskGrid` 全部程式並檢查真實 log（透過 `explorer.exe` 複製出來），修正以下各項，能測的都加了測試：
1. `ItemImporter.ShortcutName`：磁碟機根目錄（`D:\`）沒有檔名，舊的替代做法得到 `D:\.lnk` 這個路徑，`Path.Combine` 因此把捷徑放到磁碟機根目錄。現在名稱是路徑去掉檔名不能有的字元後剩下的部分（`D.lnk`）。
2. `FolderDeleter`：`Directory.Move` 不能跨磁碟機；從 D: 的根目錄移到 C: 桌面的子資料夾，改為整個複製（含隱藏檔）再刪除（`CopyThenDelete`）。複製失敗時刪掉複製到一半的，原資料夾保留，該項目照舊算移不過去。
3. `FolderWidget`：右鍵選單只在建立時用當時的文字建一次；現在每次開啟都重新設定（`LocalizeMenu`）。
4. `WidgetGrid.Candidates`：沒有圖示的格都被佔用時，退回的是那些已被佔用的格，widget 可能疊在另一個上。現在順序是：沒有 widget 也沒有圖示 → 沒有 widget → 任何格。
5. `PopupPanel.OnDrop`：放進來的東西其實沒有可加入的項目時，為它空出的位置不會收回。
6. `UpdateInstaller`：檢查碼不符時，下載檔在還開著時就被刪除（顯示「使用中」錯誤而不是不符）。現在雜湊在 `HasHashAsync` 算，先關檔再回傳。
7. `ChangeLog.Parse`：換行的句子兩邊都是中日韓文字時不加空格接起來（中文更新紀錄）；旁邊是英文字時保留空格，與中文原文寫法一致。
8. `App.ApplySettings`：開機登入時還在等桌面就緒，此時選了新根目錄不再啟動 widget（等待結束時會用新根目錄啟動），以前會出現第二份。`App.OpenPanel`：widget 還沒建立前到達的工作列請求先保留，第一次掃描後開啟，而不是丟掉。
9. `SettingsWindow.OnBrowse`：只把存在的資料夾當作資料夾選擇器的起點（含無效字元的路徑會讓它丟例外）。
10. `ShortcutTargetWatcher`：不存在的目標資料夾（失效的捷徑）直接略過、不記 log；以前每次掃描都記一行（真實 log 自 2026-11-03 起 16 次）。
另外：`PopupPanel.SlideEasing` 改為 frozen（所有面板共用；這個修正隨被 revert 的動畫 commit 一起消失了），一組 widget 放開時只讀一次桌面圖示、只存一次 config，而不是每個 widget 各一次。
**Reason**: 使用者要求做完所有能做的 issue，並找出所有 bug 修正。
**Alternatives**: 第 2 項：在子資料夾內逐項 `File.Move`（失敗時留下移了一半的，較難復原）；顯示訊息拒絕刪除（那這種根目錄就用不了這個設定）。第 4 項：保留舊順序並推開圖示（在那之前 widget 仍會互疊）。第 8 項：改在 `StartWidgetsAsync` 加防護（兩處都得知道對方）。
**Consequences**: 跨磁碟機複製遇到很大的子資料夾會慢，裡面的 junction 會被複製成內容。所有修正都沒有在真實桌面上試過（拖曳、刪除、開機流程無法從這裡操作）；Fluent 視窗只繪製成圖（兩種語言、淺色與深色）檢查版面。繪製時曾有一個建立 DeskGrid `App` 的探測程式在測試程序內跑了啟動流程，把 MSIX 虛擬化那一份 `Run` 值改指向測試主程式；已確認真實的 `Run` 值沒有變。
**Author**: 使用者要求；修正方式由 Claude 決定

---

### 2026-11-06（二）— README 截圖由 DeskGrid 自己的視窗繪製（#17）
**Decision**: README（兩種語言）新增「螢幕截圖」一節，每種語言兩張圖，放在 `docs/images/`：桌面（四個 widget，其中一個在 widget 旁開啟面板，面板背景「深色」）與完整的設定視窗（深色主題）。圖由一個不 commit 的暫時測試繪製：真正的 `FolderWidget`、`PopupPanel`、`SettingsWindow` 類別，內容是指向 Windows 內建 App（記事本、小算盤…）的捷徑，放在自訂名稱的資料夾（「工具」、「系統」…；英文圖用英文名稱），根目錄顯示為 `C:\Users\User\DeskGrid`，背景用純色漸層代替桌布，以 `RenderTargetBitmap` 2 倍繪製。設定視窗比螢幕高、會捲動，所以內容縮成 0.75 倍，再用補回來的 DPI 繪製。README 說明了圖的來源。
**Reason**: #17 要的截圖不能露出個人桌面圖示或檔名。真正的截圖需要乾淨的桌面，這台電腦沒有，也無法從這裡截；繪製真正的類別，呈現的就是 App 畫出來的樣子，而且沒有任何私人內容。
**Alternatives**: 在乾淨的測試桌面上實際截圖（仍是更好的圖，需要使用者）；手繪示意圖（就不是 App 本身了）。Windows 11 的毛玻璃無法這樣繪製，所以用「深色」背景。
**Consequences**: 圖呈現的是 0.4.1 與目前的版面；視窗改了就要重畫（探測程式記在這裡，沒有保留）。繪製時發現「更新紀錄…」與「檢查更新」沒有對齊（Fluent 樣式把按鈕放在格子左邊），兩者現在都撐滿所在的欄。#5 中能從圖判斷的部分（各卡片、啟動時檢查更新的勾選框、新增資料夾與更新對話框，兩種語言、淺色與深色）看起來都正確；在真實桌面上的表現仍待使用者確認。
**Author**: 使用者要求做完所有能做的 issue；繪製圖的方式由 Claude 決定

---

### 2026-11-06（三）— App 跟著 repo 改用 `RT-Next-Energy-Systems/DeskGrid`
**Decision**: repo 已從 `Ryan970714/DeskGrid` 搬到 `RT-Next-Energy-Systems/DeskGrid`（push 時 GitHub 提示）。`UpdateChecker.RepositoryUrl` 與檢查更新的 API 位址、兩種語言的隱私連結（`SettingsPrivacyUrl`）、`CLAUDE.md` 與專案摘要都改用新位址。（本機的 `origin` remote 仍是舊位址，透過轉址 push；要不要改由使用者決定。）`UpdateCheckerTests.PrivacyLink_IsInTheRepository` 檢查隱私連結以 `RepositoryUrl` 開頭。這裡較早的紀錄保留舊名稱：寫下時是正確的。
**Reason**: 詢問後由使用者決定。舊位址透過 GitHub 的轉址仍可使用（實測：API 回 301 到 `/repositories/<id>/…`，`HttpClient` 會跟著轉，回傳的 release 連到新位址），但只到有人取用 `Ryan970714/DeskGrid` 這個名稱為止。
**Alternatives**: 保留舊位址、依賴轉址（不必改，但之後每個新版本的檢查更新都會掛在別人帳號的名稱上）。
**Consequences**: 0.4.1 以前的版本仍查詢舊位址、依賴轉址；下一版起查詢新位址。SignPath 申請（#8）應填新的 repo。
**Author**: 使用者決定；Claude 修改

---

### 2026-11-07 — 發行版本維持三段；設定中的連結只顯示 owner/name
**Decision**: 設定視窗連結重疊的修正以 0.4.3 發佈，而不是使用者原先提的 0.4.2.1。設定中的 GitHub 連結現在顯示 `RT-Next-Energy-Systems/DeskGrid`（完整位址在滑鼠提示中），連結那一列改為 `WrapPanel`，與「更新紀錄…」按鈕之間留 12 DIP。`SettingsWindowTests.GitHubRow_FitsBesideTheChangeLogButton` 在兩種語言下檢查連結維持一行且不碰到按鈕（在 0.4.2 的英文版面會失敗）。README 的設定截圖已重畫（0.4.3、新位址）。
**Reason**: 檢查更新只比 major.minor.build（`UpdateChecker.Normalize`），0.4.2.1 會被當成等於 0.4.2，使用 0.4.2 的人不會收到更新。連結重疊是因為 repo 新位址比舊的長 11 個字元，而水平 `StackPanel` 不會在欄寬處停下。
**Alternatives**: 讓檢查更新認得第四段（為了用不到的編號方式改程式）；保留完整位址、把「隱私」移到自己一列（卡片變高）。
**Consequences**: 每次發行都要遞增前三段之一。若連結仍放不下（較長的翻譯），會換行排列，而不是跑到按鈕底下。
**Author**: 使用者要求修正並發行；版本號 0.4.3 與版面由 Claude 決定

---

### 2026-11-08 — 腳本與 AI agent 透過 `--ctl` 與 JSON pipe 控制 DeskGrid，需開啟，不能刪除
**Decision**: 新增 `Control/` 模組。`DeskGrid.exe --ctl <指令> [參數]` 送一行 JSON 到每位使用者的 named pipe `DeskGrid.Control.<使用者>`，印出一行 JSON 回覆（結束代碼 0 / 1 / 2 / 3）。指令：`status`、`list`、`new`、`rename`、`add`、`move`、`open`、`close`。要勾選設定的「允許腳本與 AI agent 控制」（config `allowControl`）才會生效。沒有刪除指令。協定訊息與 `--ctl help` 只有英文；JSON 把非 ASCII 寫成 `\uXXXX`。
**Reason**: 使用者問 DeskGrid 能否讓 AI agent 用指令控制，在 MCP server 與只改資料夾的 skill 之間選了方案 A（CLI + pipe）。資料夾本來就是一般資料夾，agent 已經能改，但不能開面板、讀順序，也無法套用同一套匯入規則。任何 agent 都能從 shell 執行 CLI，除了 DeskGrid 不需安裝別的。需要開啟是因為這是新的進入方式；不能刪除是因為使用者還沒回答要不要破壞性指令，而從介面刪除會先確認。
**Alternatives**: MCP server（方案 B；之後可疊在同一條 pipe 上）；只做 skill（方案 C；不能開面板、沒有順序）；協定訊息在地化（它是給程式用的 API，要穩定的文字）。
**Consequences**: 執行 `--ctl` 的是一個短暫的 DeskGrid 行程，在單一執行個體 mutex 之前就結束。視窗程式的輸出只有透過管線讀取的呼叫端（Bash、`| Out-String`）才拿得到，否則寫到附加的父主控台。之後若要刪除指令，需要另外決定（資源回收筒、確認）。
**Author**: 使用者選方案 A；協定、指令與開關由 Claude 設計

---

### 2026-11-08 (2) — 系統層級鍵盤快速鍵：預設 Ctrl+Alt+M，每個動作與每個資料夾各一組
**Decision**: 新增 `Hotkeys/` 模組，用 `RegisterHotKey` 註冊快速鍵，在任何程式中都有效。動作：開啟所有資料夾（預設 **Ctrl+Alt+M**，像工作列按鈕一樣切換列表）、新增資料夾、設定（無預設），以及每個資料夾一個。快速鍵必須包含 Ctrl、Alt 或 Win；主鍵是字母、數字、F1–F24、空白鍵、Enter、方向鍵或導覽鍵。「設定 → 鍵盤快速鍵 → 自訂…」開啟一個視窗，每個動作一個欄位：點一下、按下組合鍵、Backspace 清除；兩個動作不能共用一組；「還原預設值」。一般快速鍵存在 `config.json` 的 `hotkeys`，資料夾的存在它的 widget 項目（`hotkey`）。面板經由和工作列按鈕相同的路徑，開在滑鼠游標處。
**Reason**: 使用者要一套簡單、易用、直觀且可自訂的快速鍵系統，範例是「Ctrl+M 開啟所有資料夾，像釘在工作列的那顆按鈕」。每個動作一個欄位、「按下按鍵」錄製，是大家在其他程式裡熟悉的方式。資料夾的快速鍵跟著資料夾存，改名或刪除就不必另外處理。預設是 Ctrl+Alt+M 而不是範例的 Ctrl+M，因為註冊了 Ctrl+M 之後，所有其他程式都會失去它（終端機裡是 Enter，編輯器裡是縮排）；使用者仍可自己選。
**Alternatives**: 預設就用單純的 Ctrl+M（符合範例，卻害到所有其他程式）；只在桌面或面板有焦點時有效（不像工作列按鈕那樣任何地方都能用）；把所有列直接放進設定視窗的卡片（視窗本來就長，資料夾可能很多）；資料夾快速鍵用名稱存在 `hotkeys`（改名就會遺失）。
**Consequences**: 別的程式可能已經擁有某個組合：Windows 拒絕註冊，會寫 log，並在視窗按「確定」後列出被拒絕的。視窗開著時所有快速鍵都暫停（否則 Windows 會搶走正要錄製的按鍵）。按下快速鍵後面板是否拿到前景、視窗的外觀，還沒在真實桌面驗證。使用者是否寧可預設用 Ctrl+M，待確認。
**Author**: 使用者要求這個功能；動作、預設值、儲存方式與視窗由 Claude 決定

---

### 2026-11-08 (3) — 「開啟所有資料夾」的預設快速鍵改為 Ctrl+M
**Decision**: `HotkeyBindings.DefaultAllFolders` 改成單純的 **Ctrl+M**，取代紀錄 (2) 的 Ctrl+Alt+M。其他都沒變：仍可在「設定 → 鍵盤快速鍵」清除或更換。README 提醒 DeskGrid 執行期間其他程式收不到 Ctrl+M，並建議可改用 Ctrl+Alt+M。
**Reason**: 紀錄 (2) 把這個問題留著；使用者在兩個選項中選了 Ctrl+M，也就是他一開始要的快速鍵。
**Alternatives**: 維持 Ctrl+Alt+M（對其他程式安全，但不是使用者要的）。
**Consequences**: 執行 DeskGrid 的人在其他程式裡就失去 Ctrl+M，除非自己改掉。從沒存過快速鍵的 config 會立刻拿到新預設；存過選擇的則保留原選擇。
**Author**: 使用者選擇；改動由 Claude 完成

---

### 2026-11-08 (4) — 改名時立刻搬設定；版本 0.5.0
**Decision**: `App.RenameFolder` 在資料夾搬完後立刻呼叫新的 `WidgetCatalog.MoveSettings(舊, 新)`，讓位置、項目順序、快速鍵與在資料夾順序中的位置，在任何重新掃描之前就跟著改名。檔案監看器的改名事件稍後仍會到，但已經沒有東西可搬。資料夾沒有快速鍵時，`config.json` 不再寫 `hotkey`。快速鍵視窗的欄位與 × 按鈕有給螢幕閱讀器用的名稱，每次按下快速鍵都會寫 log（`Shortcut pressed: <動作>`）。版本定為 0.5.0（新功能：快速鍵與控制管道）。
**Reason**: 在真實桌面測 `--ctl rename` 時發現，改名後的資料夾項目變成依字母排序、沒有位置、沒有快速鍵：指令在搬完後立刻重新掃描，監看器的改名事件還沒被處理，`Reconcile` 就把舊名稱的設定當成消失的資料夾丟掉。從面板改名之所以安全，只是因為它自己不重新掃描。版本是 0.5.0 而不是 0.4.4，因為自 0.4.3 以來的改動是新增功能。
**Alternatives**: 讓 `--ctl rename` 等監看器（憑時間猜測）；拿掉指令裡的重新掃描（下一次 `list` 的答案會是舊的）；讓 `Reconcile` 去猜改名（它分不出改名與刪除再新增）。
**Consequences**: 之後任何會改資料夾名稱的程式都必須一併呼叫 `MoveSettings`。沒有它時 `WidgetCatalogTests.RescanRightAfterRenamingAFolder_KeepsItsSettings` 會失敗。`LogTests`（會換掉靜態的 log 路徑）改放進 `ShellCollection` 單獨執行：新增的 log 讓別的測試同時寫 log 時，它偶爾會失敗。
**Author**: 使用者要求真機測試、修 bug 並定為 0.5.0；bug 由 Claude 找到並修正

---

### 2026-11-09 — 更新後顯示新功能視窗，每個版本附「使用方法」
**Decision**: DeskGrid 記住上次顯示「新功能」的版本（`config.json` 的 `lastSeenVersion`）。執行中的版本與它不同時，在 widget 都出現後，依介面語言用 `ChangeLogWindow` 顯示「記住的版本之後、到執行中版本」之間每一版的內容。第一次執行只記錄版本；沒有這個欄位時代的設定只顯示執行中版本自己的那一段。每個新增功能的版本，更新紀錄都要有 `### How to use` 小節（中文版是 `### 使用方法`），每個功能一條簡短的話；`ChangeLogTests` 檢查最新版本。規則寫在 `AGENTS.md` / `AGENTS.zh-TW.md`。
**Reason**: 使用者要求每次更新後依所選語言出現更新紀錄，而且新功能要附上越簡單越好的使用說明。以版本為依據（而不是更新安裝程式）連手動換檔也涵蓋，不必多一個機制。
**Alternatives**: 讓更新腳本傳旗標給新行程（漏掉手動更新、零件更多）；只顯示執行中版本（跳版本的使用者會漏掉中間的）；使用說明只放 README（更新當下沒人會去讀）。
**Consequences**: 之後的版本要在開發功能時就寫 `### How to use`。視窗顯示前先存版本，所以當機不會讓它重複出現。尚未在真實桌面上真的更新一次來看。
**Author**: 使用者提出；Claude 設計並實作

---

### 2026-11-09 (2) — 跟隨系統、深色、淺色背景的不透明度
**Decision**: `panelBackground.opacity`（可為 null，未設定時不寫進檔案）設定除了自訂以外每種樣式的不透明度；自訂仍用 `customOpacity`。設定的滑桿一律可用，只有顏色按鈕屬於自訂。深色與淺色把這個值當成顏色的 alpha。跟隨系統（DWM 的 Acrylic）一旦選了數值，就改成一般半透明的系統表面色；滑桿下有一行提示說明。與樣式預設值相差不到半格滑桿的值存成未設定，所以除非使用者真的動了滑桿，仍維持 Acrylic。
**Reason**: 使用者要求在背景不是自訂時也能設透明度。Acrylic 的毛玻璃沒有不透明度可調，跟隨系統無法就地變得更透明或更不透明；改成純色半透明是讓滑桿在那裡有意義的唯一做法。
**Alternatives**: 只給深色 / 淺色用滑桿（把預設樣式排除在外）；在 Acrylic 上疊一層色（只能更不透明，滑桿有一半沒作用）；所有樣式（含自訂）共用同一個不透明度（會改變既有 config 的意思）。
**Consequences**: 跟隨系統選了不透明度就失去毛玻璃。各樣式的預設（系統顯示 85 %、深色 82 %、淺色 80 %）在 `PanelTheme.DefaultOpacity`。尚未在真實桌面上看過。
**Author**: 使用者提出；Claude 設計並實作

---

### 2026-11-09 (3) — 液態玻璃面板背景：在 CPU 上以兩個 MIT 專案為基礎繪製
**Decision**: 新增面板背景樣式 `liquidGlass`。面板在顯示之前讀一次它後面的畫面，由 `LiquidGlass.Render`（純 C#，不用 shader、不加新依賴）依那張畫面畫出背景：模糊、沿邊緣法線依 OverShifted/LiquidGlass 的衰減曲線在邊緣彎折，加上它的方向性邊緣光暈、一圈亮邊與輕微提亮，再依「不透明度」設定混入表面色（預設 25 %）。文字依玻璃實際呈現的畫面變成深色或白色。程式碼改寫自 OverShifted/LiquidGlass（MIT）與 shuding/liquid-glass（MIT）；它們的聲明與授權條文放在 README 的「第三方聲明」，設定與 Release 說明有連結，著作權行也在原始檔開頭。
**Reason**: 使用者要求做 iOS 風格的玻璃效果，並要依開源專案的授權使用它們。這兩個 MIT 專案是授權乾淨的。`dragosniamtu/WPF-Liquid-Glass-Effect`（WPF、MIT）沒有採用：它的 shader 改編自一篇沒有程式碼授權的 Medium 文章，它自己的聲明就說散布要取得許可，而且需要一個二進位檔不是 MIT 授權的 NuGet 套件。自己是一個視窗，沒辦法即時看到後面的畫面而不拍到自己，所以在顯示之前拍一張。
**Alternatives**: 像素 shader（repo 裡要放編譯好的 `.ps`，或建置時要有 HLSL 編譯器）；用 Windows.Graphics.Capture 即時擷取（面板會被拍進去，依賴也比較重）；用 DWM 的 acrylic（它不能彎折任何東西）。
**Consequences**: 玻璃是一張快照：開著的面板後面有東西在動不會顯示。那張畫面（2880x1800 螢幕約 20 MB）只在面板開著時保留。任何散布程式的方式都必須附上這些聲明。尚未在其他 DPI、第二個螢幕或明亮視窗上看過。
**Author**: 使用者提出；Claude 調查、選擇並實作

### 2026-11-09 (4) — 液態玻璃的邊緣暗度設定；設定視窗改成分頁列表
**Decision**: 液態玻璃邊緣暗側的暗度改成設定（`panelBackground.glassShade`，0 到 1，未設定 = 50%），不再是固定值。設定視窗改成左邊四個分頁的列表（一般、面板、快速鍵與控制、關於），右邊顯示選到的那一頁。
**Reason**: 使用者看到玻璃右上邊緣偏暗，想自己決定暗度（第一次修正是把它減半），也要求左邊做工具欄、每一項對應一類設定，因為單一長串卡片已經太長。
**Alternatives**: 固定較低的值（有人喜歡那種立體感）；上方的頁籤（放更多分頁的空間較小）；樹狀結構。四個分組是 Claude 依現有項目決定的，沒有先問。
**Consequences**: 每個控制項的名稱不變；沒顯示的分頁是 collapsed，所以要量排版的測試得先顯示該分頁。視窗固定 780 x 620。README 裡設定視窗的截圖已經過時。
**Author**: 使用者提出；Claude 決定分組

---

### 2026-11-10 — 桌面的東西改為搬進去；資料夾也是項目；預覽在背景繪製；文字縮短
**Decision**: (1) 直接放在桌面上的東西拖到資料夾上，不管是什麼（捷徑、檔案、`.exe`、資料夾）都搬進去；`dropMode` 的複製現在對它們全部代表「桌面保留」（捷徑複製，其他建捷徑）。其他地方來的不變：絕不移動本體。widget 資料夾裡的子資料夾算項目。(2) 設定的預覽改在執行緒集區畫液態玻璃，只畫最新的要求；`LiquidGlass.Render` 改用租用的緩衝區與 `Parallel.For`。(3) 兩種語言的介面文字都縮短；「資料夾展開位置」與「邊緣暗度」底下的提示拿掉。
**Reason**: 使用者回報桌面資料夾拖進去後桌面與 DeskGrid 同時都有，並要求拖進來的東西絕不兩邊同時存在；選了「只有桌面上的」、「桌面的 .exe 也搬」、「保留設定並適用全部」。另一位使用者回報修改預覽時電腦當掉；實測：滑桿每一步在 UI 執行緒約 80 ms 加一次 gen-2 回收。使用者要求文字更短、少一點機器味。
**Alternatives**: 任何位置的本體都搬（會弄壞已安裝的程式、跨磁碟機複製大資料夾）；保留捷徑並隱藏桌面本體（兩樣東西要同步）；在 UI 執行緒上延遲重畫預覽（每次繪製仍會卡住）。
**Consequences**: 對放在桌面上的程式，這取代了先前「絕不移動 .exe」的規則。原本就在 widget 資料夾裡的子資料夾現在會顯示成項目。當機是依量測找到的原因修正的；沒有在回報者的電腦上重現。
**Author**: 使用者提出並選定規則；Claude 設計並實作

### 2026-11-10（二）— 提示島：螢幕頂端的膠囊，訊息由 `--ctl` 送入；自己重寫，不取用 BrimDeck
**Decision**: DeskGrid 新增「提示島」：主螢幕頂端正中央的黑色膠囊，顯示一行字幾秒鐘（`Island/`；config `islandEnabled`，預設關閉）。訊息只來自 `DeskGrid.exe --ctl island show <文字> [秒數]`（以及 `island hide`）；多則訊息排隊；游標移上去膠囊會展開顯示完整文字並暫停倒數；全螢幕程式執行時隱藏。全部為 DeskGrid 自行撰寫；BrimDeck 專案只讀來取得想法。
**Reason**: 使用者想要類似靈動島的功能，在討論過 Windows 允許什麼（沒有 Live Activities API，資料只能來自系統介面或我們自己）和現有專案之後，選了第一步（「P1 骨架」：一種外觀、游標展開、緩動、全螢幕防護、通用佇列加控制管道推送）。BrimDeck（Apache-2.0）最接近，但它是一個 4.7 萬行的獨立應用程式，會修改其他程式的 `libcef.dll`、載入核心驅動（PawnIO）讀感測器、讀其他工具的登入資料，並為了歌詞與額度連網；這些都不符合 DeskGrid「預設不連網、不動其他程式」的規則，它的結構（一個視窗拆成 20 多個 partial 類別）也不適合 DeskGrid。
**Alternatives**: 直接 fork 或複製 BrimDeck（授權沒問題，其他都有問題）；用應用程式工具列保留螢幕空間（所有視窗會縮小、widget 格線會移位）；平常也顯示一顆小膠囊（會蓋住分頁列，暫不採用，沒有訊息時什麼都不顯示）；改用 Windows toast（我們無法調整樣式或排隊）。
**Consequences**: 新增一種「在其他視窗上面」的頂層視窗（最上層、不啟用、分層）：開始功能表、Alt+Tab、安全桌面和 UAC 仍然會蓋住它。全螢幕規則只看 Shell 回報什麼。顯示訊息時膠囊會蓋住頂端正中央底下的東西。後續步驟（透過系統媒體控制顯示正在播放、電池、把資料夾面板當成展開畫面、快速鍵）是計畫中、尚未實作；BrimDeck 的修改程式、驅動和讀登入資料都不會複製。
**Author**: 使用者提出並選定第一步；Claude 研究、設計並實作
