# BrimDeck inside DeskGrid

This folder holds the source of [BrimDeck](https://github.com/MoFeng2223/BrimDeck) (copyright its authors, Apache License 2.0,
see `LICENSE`), copied from its `main` branch (commit `f1510d0`, version 1.3.2). DeskGrid ships it as **NotiLand**:
DeskGrid starts it when "Show NotiLand" is on, and it runs as a program of its own.

## What was changed

The code is BrimDeck's own. Only what the integration needs was changed:

- **Removed** (the privileged or other-program-changing parts): `LibcefPatch`, `KugouPatch`, `NeteasePatch`, `LibcefFile`,
  `KugouInstall`, `NeteaseInstall` (they change another program's `libcef.dll`), `SensorSetup`, `SensorClient` and the whole
  `BrimDeck.Sensors` project (a service installed with administrator rights, using a kernel driver). `Native/DisabledFeatures.cs`
  stands in for them with the same signatures, and every one of them answers "not available". The sensors settings page
  (`SettingsSensors.cs`) is reduced to the helpers other pages use and left out of the page list.
- **A library inside DeskGrid's process** (2026-11-20; it first ran as a program of its own beside DeskGrid): `BrimDeck.csproj` is a
  class library that DeskGrid references, and BrimDeck's `App` is no longer a WPF `Application` (a process has one): it is a plain
  class started by `App.StartHosted()` on DeskGrid's UI thread and stopped by `ExitApplication()` (with the panels' power-off) or
  `StopNow()`, reporting `Stopped`. `Program.cs` is gone; the two helpers BrimDeck started as copies of itself (portable update, the
  administrator one for frame rates) are run by DeskGrid's start-up through `App.TryRunHelper`. A BrimDeck of its own that already
  runs (it keeps the data folder) keeps NotiLand from starting.
- **Settings are pages in DeskGrid's settings window** (2026-11-20): BrimDeck's `SettingsWindow` (a `Window` with its own page list, caption
  buttons and size) is `SettingsPages`, a `UserControl` that shows one page at a time and implements `IDeckSurface` (so `DeckWindow.Holds`
  treats it like one of BrimDeck's windows: tips and menus). The host lists the pages (`SettingsPages.Pages()`: page number, group
  caption, title), calls `ShowPage` / `Leave` / `HostDeactivated` / `Refresh` and gives it back with `Release`; `App.CreateSettingsPages()`
  makes it, and `App.SettingsRequested` (the gear, the tray menu, a hotkey, a link on a page) asks the host to show it. Dialogs a page opens
  are owned by the host's window (`DialogOwner`). The pages follow the system's light or dark theme, not the Theme setting (`HostIsDark`).
  The pages are drawn like DeskGrid's own (`SettingsNative.cs`): inside `SettingsPages`, BrimDeck's style names (`SettingsButton`,
  `SettingsToggle`, `SettingsCombo`, `SettingsField`, `SettingsSlider`, `SettingsSegment` ...) stand for the Fluent styles of the system's
  theme (`FindResource` is hidden by one that answers them from `PresentationFramework.Fluent`), a group of rows is a DeskGrid card,
  there is no page heading, `SettingsPalette` is Fluent's greys with the system accent, and `DeckResources` (BrimDeck's implicit styles
  for Button, TextBox, ScrollBar, ToolTip ...) is no longer merged into the pages. What BrimDeck drew itself with no ordinary control
  (choice cards with a sketch, chips, steppers, clock tiles, the number box, the price table) keeps its template, in that palette.
  Removed because DeskGrid has them: the About page (version, update
  check, links; `SettingsUpdates.cs` and `SettingsReleaseNotes.cs` deleted), the Start with Windows switch, the Exit row and the language
  choice (`App.StartHosted(english)` and `App.FollowLanguage` take DeskGrid's language). The caption, the brand and the window's size and
  position settings (`SettingsWindowWidth` ...) are no longer used.
- **No tray icon of its own** (2026-11-21): `App.ApplyTray` returns at once when `ManagedByDeskGrid`, and the *Show tray icon* switch is out of the General page (`SettingsPages.General`). `TrayIcon.cs` stays in the tree, unused.
- **Stand-in panel on another monitor** (2026-11-21): when a window rule hides every panel on show (`MainWindow.HiddenByContext`, raised by
  `ContextHideChanged`), `App.UpdateStandIn` shows one more panel on a monitor no panel owns (`DisplayLayout.StandIn`), kept in
  `_otherDecks` under `StandInKey`, and retires it when one of the others is back. Seen on the real desktop with a full-screen browser.
- **One screenshot, one copy** (2026-11-21): the snipping tool writes the same picture to the clipboard twice about 125 ms apart;
  `ShelfService.Copied` keeps it once (`ClipDuplicates`).
- **A capture that hears nothing, or whose process ended, is replaced** (2026-11-21): `CaptureWatch`, `AudioLevelMeter.Retarget`, `Alive`, `FindPlayingPid`; see `context/decisions.md` (2026-11-21 (4) and (5)).
- **No session of the player found = hear the whole system** (2026-11-23): `AudioLevelMeter.Enumerate` (capture of everything but this process, the devices' peak meters without a capture), `App.Trace` (the meter's findings go to the host's log); see `context/decisions.md` (2026-11-23).
- **Styles stay in BrimDeck's windows** (`DeckResources.xaml`, was `App.xaml`; `DeckResources.cs`; `DeckWindow.cs`): its implicit styles
  (Button, TextBox, ScrollBar, ToolTip, ...) would restyle DeskGrid's own windows if they were the application's resources. They are
  one shared `ResourceDictionary` that every BrimDeck window merges into its own resources (`DeckWindow` is the base of all of
  them; `MainWindow.xaml` merges it with `x:Static`, since its XAML replaces the window's resources). The theme and the fonts are
  published into it (`PanelTheme`, `Fonts`), the tooltip timing is set on BrimDeck's windows only (it was `OverrideMetadata` on every
  element), and the global tooltip / menu handlers act only on elements of BrimDeck's windows (`DeckWindow.Holds`).
- **Other process-wide things dropped or fixed**: the explicit AppUserModelID, the handled-exception behaviour of
  `DispatcherUnhandledException` (it only records now), the version (`ApplicationVersion.UseAssembly` from a module initializer: the
  entry assembly is DeskGrid), the tray icon (extracted from BrimDeck's own icon resource, not the exe's). BrimDeck does not register
  itself to start with Windows (`StartupRegistration`) nor check for updates on its own (`StartBackgroundUpdates`): DeskGrid does both.
- **Traditional Chinese**: every Simplified Chinese text of the copied source (interface texts, labels, comments, the built-in
  themes' names) was converted to Traditional Chinese with OpenCC (`s2twp`), so the Chinese interface of NotiLand is Traditional.
  Left as they are, because they match text of other programs: NetEase Cloud Music's log lines and its registry display name,
  the lyric notices of NetEase, and the quotes of NetEase and KuGou's own (Simplified) menus. The keyword tables of
  `Devices.cs` and `PhoneLinkCard.cs` keep the Simplified words and got the Traditional ones added. The settings tag for the
  Chinese interface is still `zh-CN` (it is stored in BrimDeck's settings), the text is Traditional.
- **Not copied**: BrimDeck's tests, documentation, installer and scripts.
- `Directory.Build.props` and `.editorconfig` keep DeskGrid's build rules (warnings as errors, style) off this folder.

To take a newer BrimDeck: copy its `src/BrimDeck`, `src/BrimDeck.Core` and `releases` over these folders, delete the files listed
above, and re-apply the changes above (`git diff` of the previous import shows them).
