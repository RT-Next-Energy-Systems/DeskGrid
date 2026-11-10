# Project Summary

> Language policy: this file and `project-summary.zh-TW.md` are twins — change both together. See "Language policy" in `AGENTS.md`.

## Project Name
DeskGrid (repo directory name DeskGrid)

## Project Goal
Put iOS/Android-style folder widgets on the Windows 10 22H2 / 11 desktop: a 2×2 thumbnail preview, a grid panel that opens on click, and every widget backed by a real folder that stays in sync live. Open source under the MIT license.

## Project Description
See the beginning of `CLAUDE.md` and its "Behavior rules".

## Architecture
See "Architecture" and the per-stage implementation notes in `CLAUDE.md` (the single source; not repeated here).

## Tech Stack
C# + WPF, .NET 10 LTS, x64, CsWin32, xUnit v3, self-contained single exe. Details in "Tech stack" in `CLAUDE.md`.

## Directory Structure
```
DeskGrid.slnx          solution
Directory.Build.props     shared settings (nullable, x64, warnings as errors)
global.json               SDK 10.0.x (rollForward latestFeature)
.github/workflows/        ci.yml (build/test/publish), release.yml (v* tag → GitHub Release)
README.md, README.zh-TW.md, LICENSE (MIT)
CLAUDE.md, AGENTS.md, CHANGELOG.md        English; each has a Traditional Chinese twin NAME.zh-TW.md
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
  Resources/              Strings.resx (en), Strings.zh-TW.resx, UiLanguage (picks the language)
  Settings/               SettingsWindow (XAML), NewFolderDialog (XAML), ColorDialog
  Shell/                  ShellImage, ShellItem, ShortcutFile, RecycleBin
  Threading/              DispatcherThread (STA worker)
  Tray/                   TrayIcon, AutoStart
  Widgets/                FolderWidget (XAML), WidgetBoard, WidgetGrid, IconMerger
tests/DeskGrid.Tests/  xUnit v3
tools/                    make-icon.ps1
context/                  session context docs (decisions and this summary are version-controlled, each in two languages)
```

## Core Modules
DesktopHost (`Desktop/`), ConfigStore (`Config/`), FolderScanner + WidgetCatalog (`Folders/`), IconService (`Icons/`), FolderWidget + WidgetBoard (`Widgets/`), PopupPanel + PanelController (`Panel/`), TrayIcon + AutoStart (`Tray/`), settings (`Settings/`), drag and drop (`Folders/ItemImporter`), keyboard shortcuts (`Hotkeys/`), the island (`Island/`). The other modules are in "Architecture" in `CLAUDE.md`.

## Coding Rules
See `AGENTS.md`.

## Current Milestone
Stage 7: CI, packaging, README, LICENSE (the last stage of the MVP).

## Current Status
Stages 1–6 were verified by the user. Stage 7 is done locally: the single-file exe is packaged and runs, CI and Release workflows, README (en / zh-TW), MIT LICENSE; still to do: push to the public repo `RT-Next-Energy-Systems/DeskGrid` (then `Ryan970714/DeskGrid`) and confirm CI passes.

## Known Issues
- The legacy desktop hierarchy (Win10 / Win11 ≤ 23H2) code path has not been tested on a real machine.
- A desktop icon that lands on a widget is moved to the nearest free cell (with "Auto arrange icons" on, the widget gives way instead); an icon placed there while DeskGrid wasn't running is avoided by the widget at startup rather than moved.
- Widgets stay visible when "Show desktop icons" is turned off; it is undecided whether they should hide too.
- Mounting a child window across processes links the input queues: when Explorer hangs, our UI thread may hang with it. `0x052C` blocks for up to 1 second.
- Positions/sizes are computed with the system DPI; changing DPI while running and multiple monitors are not handled (outside the MVP).
- A folder/item renamed while the app is not running can't carry its settings over (the next start sees a delete plus an add).
- A widget folder that can't be read (insufficient permission) counts as missing during that scan, and its settings are purged.
- Deleting the root folder itself while running: the watcher stops and does not recover on its own (restarting the app recreates it).
- When a shortcut target program updates its icon but the .lnk itself is unchanged, the cached old icon is only refreshed after the app restarts.
- The icon cache is in memory only; every start fetches icons from the Shell again (once per item).
- A program with only small icons gets upscaled by the Shell at 256px and may look blurry (the local samples all have 256px icons).
- Widgets are not re-snapped when the resolution, DPI or taskbar position changes while running; restarting the app clamps them back into the work area.
- Multiple monitors: cell sizes use each monitor's DPI, but the widgets are actually rendered at Progman's DPI, so they may be misplaced on secondary monitors with another DPI.
- A drag can't be cancelled with Esc; dropping on an occupied cell puts the widget in the next nearest free cell.
- When another widget is dragged while a panel is open, the panel doesn't close automatically.
- The panel background reads the settings and the system light/dark theme only when it opens.
- Dragging an app (UWP / non-file-system item) from the Start menu onto a widget is not supported (no file path).
- Running as administrator, UIPI blocks files dragged in from File Explorer.
- The tray menu is a native Win32 menu and stays light in dark mode; the settings window has the classic WPF look (light).
- Changing the root folder rebuilds all widgets (a brief flicker).
- On Win10 the "system" background takes the opaque system color path; not seen on a real Win10 machine.
- Inside the panel only Tab / Enter work from the keyboard; there are no arrow keys.

## Future Plan
Per "Development stages and progress" in `CLAUDE.md`, 3 → 7.

---
*Keep this file short (1–3 pages) and record only project facts that never change or change slowly. For the work log and progress details see `current-task.md`, `progress.md`, `session-log.md`.*
