# Changelog

## 0.9.7

### Added

- **The AULA (狼蛛) HERO 68 Air keyboard shows its battery** on NotiLand's devices page, on its 2.4 GHz receiver (8K, 372E:106C). The receiver talks in fragments that each have to be acknowledged, which DeskGrid did not do before; it reads the level and whether the keyboard is charging. Seen on a real keyboard (35 %). USB and Bluetooth are not read.
- **A keyboard that is asleep is listed as asleep** (with its last level once it has had one), and is read again at every pass. A device that had never answered used to be tried three times and then given up on until it was plugged in again, so a keyboard that slept when DeskGrid started stayed on "battery cannot be read".

### How to use

- Nothing to set. With NotiLand on (**Settings → NotiLand**), put the keyboard in its 2.4 GHz mode: it appears on the devices page, and a key press wakes it if it sleeps. Other keyboards of the same receiver (HERO 68 XS, 75 HE, MINI Air) are not read.

## 0.9.6

### Added

- **Three more devices show their battery on NotiLand's devices page:** the Poly Voyager 8200 UC (behind its BT600 adapter), the MOJHON GALE2 gamepad (2.4 GHz) and the EPOS H3PRO Hybrid (dongle or cable). Their protocols come from open-source projects (HaloBattery, vigil-watt, HeadsetControl) and every frame is checked in tests, but they have **not been run on the real devices yet**: if one does not show, or shows a wrong level, report it with its vendor and product id.

### How to use

- Nothing to set. With NotiLand on (**Settings → NotiLand**), plug in or turn on the device and it appears on the devices page. A keyboard or mouse whose model is not listed is not read: 2.4 GHz receivers have no common standard, so each model needs an entry of its own.

## 0.9.5

### Changed

- **No more line under a pinned taskbar button.** 0.9.4 kept a hidden window for each pinned button so a click was instant, and Windows marks such a button as running (a line under it). That is gone, and so is the setting **Instant taskbar buttons**: a button starts the small launcher again, about 20 ms slower than a folder on the desktop (77-93 ms).

### Fixed

- A second click on a taskbar button on another monitor did not close the panel, it opened it again. It closes it now, on every monitor.

### How to use

- Nothing to set. A button you pinned works as before; if you unticked or ticked **Instant taskbar buttons**, the setting is gone and ignored.

## 0.9.4

### Added

- **Instant taskbar buttons.** A button you pinned from DeskGrid now opens its panel about as fast as a folder on the desktop (a click to the panel's first pixels: 58 ms for the button, 66 ms for a desktop folder). DeskGrid keeps a small hidden window for each pinned button, so a click on it is a message to DeskGrid instead of a program Windows has to start first. The price is that Windows marks the button as running (a dot under it). **Settings → Shortcuts → Instant taskbar buttons** (on by default) turns it off, and the button starts a small program again, a few tens of milliseconds slower.

### Changed

- **Less memory.** Idle private memory fell from 195 to 129 MB, and the 140 MB a panel left behind for minutes is given back about 20 seconds after you stop using DeskGrid (to about 15 MB). Nothing you can do changed, and clicks are as fast as before (56-70 ms). The cost is the download: the exe is about 189 MB instead of 83 MB, because it is no longer compressed (a compressed exe is unpacked into memory every time it starts). Every start is about 130 ms faster.
- **Opening a panel is faster** (44 → 29 ms inside DeskGrid): the liquid glass reads only the part of the screen around the panel, not the whole screen. A taskbar button starts a small launcher (6 KB) instead of DeskGrid itself; a button pinned before keeps working and also starts faster.
- **New command `DeskGrid.exe --ctl memory`** reports the memory DeskGrid uses (for scripts and AI agents, when control is allowed).

### Fixed

- **NotiLand's level bars could stay flat under Apple Music on another computer.** When none of the player's sound sessions could be recognised (another version of the player, another Windows), the bars had nothing to read. NotiLand now listens to everything but DeskGrid itself in that case, looks for the player's session every 3 seconds and switches to it when it shows up, and follows the sound devices' meters on a Windows too old to capture a program's sound. What it found is written to the log (`NotiLand audio:`), so a case that is still flat can be told apart.
- Restoring settings from a backup did not apply **Instant taskbar buttons** until DeskGrid was restarted.
- A folder name that is too long showed Windows' own error message instead of being refused with the usual one.
- The first button you pin, when Windows has no folder for pinned buttons yet, did not get its instant window until DeskGrid was restarted.
- The small launcher of a taskbar button crashed when DeskGrid was run as administrator.
- When the settings file stayed locked (a virus scanner, a sync client), DeskGrid crashed at start with no explanation. It now says so and stops, so that your settings are not replaced by the defaults.
- Turning **Show NotiLand** off and on again while it was still shutting down left it off.
- Renaming a folder left the shortcut made for pinning it under the old name.

### How to use

- Pin a button as before (right-click a folder → **Pin to taskbar…**, or **Settings → Shortcuts → Pin all folders…**, then drag the shortcut onto the taskbar). It opens instantly; the dot under it is Windows saying DeskGrid is running. Buttons you pinned earlier are instant too, with nothing to do. To switch it off, untick **Instant taskbar buttons** in **Settings → Shortcuts**.
- Nothing to set for the memory: it is given back by itself. The first download of this version is larger.

## 0.9.3

### Added

- **NotiLand stays in sight on another monitor while a full-screen app hides it.** With a full-screen video (or a game) on the monitor NotiLand sits on, it used to disappear altogether, even while you worked on the other monitor. When every NotiLand on show is hidden that way and another monitor has none, NotiLand now shows there (the monitor under the pointer, if there is a choice) and goes away again as soon as the full-screen app is gone. A monitor that is full-screen too, or one you hid NotiLand on yourself, shows nothing.

### Fixed

- **NotiLand's level bars stayed flat for good after Apple Music restarted its background program (AMPLibraryAgent), and could with any player whose sound moved to another process.** The bars read the player's sound through a capture of one process, chosen once when playing starts. When that process ended (Apple Music restarts its agent: after the app is restarted, an output change or a crash), the capture went on without an error and without sound, the bars reported as running and never moved, and nothing looked for the new process. Now NotiLand notices a process that has ended and looks for the player's sessions again (every 5 seconds at most), notices a capture that has been silent for 2 seconds while the player plays on another process, and captures another process of the player when its own level meters hear sound that the capture does not (failing that the bars follow the level meter). Seen on the real machine: with the agent killed the old build stayed flat on the dead process; with Apple Music closed and opened again the bars were back on the new agent and moved as soon as the song played.
- **A screenshot copied to the clipboard (Win+Shift+S) showed twice in NotiLand's clipboard history.** The snipping tool writes the same picture to the clipboard twice, a moment apart, and each write was kept as a copy, sometimes under a different source. A picture equal to the one kept in the last two seconds is now the same copy, kept once (under the first source that is known).

### How to use

- Nothing to set: NotiLand moves to another monitor by itself while a full-screen app on its own monitor hides it. Screenshots to the clipboard now show once in the clipboard history.

## 0.9.2

### Changed

- **NotiLand no longer has a tray icon of its own.** With **Show NotiLand** on, a second icon (BrimDeck's) showed among the tray icons next to DeskGrid's. DeskGrid's is now the only one, and the **Show tray icon** switch is gone from NotiLand's General page. NotiLand is opened and set up from DeskGrid's Settings (its gear opens them too); what its own tray menu offered (hide it, click-through, switch profile, exit) is not there any more.

### Fixed

- **Turning the display to portrait (or changing the icon spacing or the scaling) rewrote the places of your folder widgets for good.** DeskGrid snapped every widget to the cells of the new layout and saved that, so turning the display back did not bring them back, and in portrait they ended up stacked in one column. A saved place now changes only when you drag that widget; in another layout the widgets are shown on the nearest free cells and go back where they were when the layout does. Anything picked with the rubber band or lit under the pointer is cleared when the layout changes. (Places already rewritten by an earlier version are not restored: move the widgets back once.)
- Folder widgets could sometimes not be moved to the leftmost and rightmost columns of the screen, and could then do so again by themselves. The cells widgets snap to follow the lattice the desktop's icons sit on, and that lattice was taken from the first icon in Explorer's list: while that icon was off the lattice (being dragged, just created, Explorer arranging the icons) every cell was shifted by that much, which left a gap at both edges. The lattice is now the one most icons are on, and a desktop whose icons are scattered (no grid alignment) has none. When Explorer does not answer for a moment, the last grid that was read is kept instead of the built-in one, so a widget let go at that moment is no longer saved on the wrong cells. The log line `Desktop icon grid` now also says where the lattice is and how many icons are off it.

### How to use

- Nothing to set: your folder widgets now keep their places when the display is turned or the layout changes. If a widget is not where you put it, drag it back once. NotiLand's tray icon is gone; open and set it up from DeskGrid's Settings.

## 0.9.1

### Fixed

- **The 0.9.0 download did not start on any computer** (it ended at once with `FileNotFoundException: BrimDeck.Core`). The release was built with a version number that left one of NotiLand's two parts out of step with the other. A copy of 0.9.0 you have already downloaded is no use; this version has the same content and starts. The build now has a check that the published exe starts and stays up before a release is made.

### How to use

- Delete the 0.9.0 exe you downloaded and download `DeskGrid-0.9.1-win-x64.exe`.

## 0.9.0

### Changed

- **NotiLand now runs inside DeskGrid.** BrimDeck was run as a second program beside DeskGrid; its code is now part of DeskGrid's own process: one program in the task manager, no second copy inside the exe (**DeskGrid.exe is about 79 MB again, down from 163 MB**), and starting and stopping it with **Show NotiLand** is instant. BrimDeck's styles apply only to its own windows, so DeskGrid's windows look as before. Its tray icon, settings and data (`%LOCALAPPDATA%\BrimDeck`) are unchanged.
- **NotiLand's settings are part of DeskGrid's Settings.** BrimDeck's own settings window is gone: its pages (general, collapsed and expanded look, panel theme, shortcuts, displays and profiles, AI usage, model prices, music, system monitor, shelf layout, clipboard, file transfer, clipboard sync, device batteries) are entries below **NotiLand** in the list on the left of DeskGrid's Settings, while NotiLand runs. They are drawn like DeskGrid's own pages (Fluent cards, check boxes, radio buttons, drop-downs and sliders in the system's light or dark theme and accent colour); BrimDeck's own styling is gone from them. The list on the left folds NotiLand's pages away and back (the arrow beside **NotiLand**, or the Left and Right keys), and its scroll bar no longer runs over the chosen entry. NotiLand speaks the language chosen in DeskGrid. What DeskGrid already has was taken out of BrimDeck's pages: its About page with its update check, its Start with Windows switch, its Exit button and its language choice. The settings window is wider (980 by 720) to fit them.

### How to use

- Settings → **NotiLand** → **Show NotiLand** works as before. Once it runs (after **Apply**) its pages are listed below it: choose one to set what NotiLand shows and how it looks. NotiLand's gear and its tray menu open them too.

## 0.8.0

### Changed

- **NotiLand is now BrimDeck.** DeskGrid's own island was replaced by [BrimDeck](https://github.com/MoFeng2223/BrimDeck) (Apache-2.0), which DeskGrid carries and runs beside itself while **Show NotiLand** is on: AI tool quotas, music, system readings, device batteries, a clipboard history and a file transfer. The parts of BrimDeck that change another program's files or install a service with administrator rights are left out (see `third_party/BrimDeck/DESKGRID.md`). The old island's other settings, shortcuts and `--ctl island` commands are gone; BrimDeck is set up in its own settings window. DeskGrid.exe is about 95 MB bigger because it carries BrimDeck.
- All Simplified Chinese in the repository is now Traditional Chinese. NotiLand's Chinese interface (texts, labels, comments, built-in theme names and its release notes) is Traditional; only the text that matches other programs (NetEase's log lines and registry name, lyric notices, the quotes of NetEase and KuGou's own menus) stays Simplified, and the device and Phone Link keyword lists have both.

### Fixed

- An icon that was dragged off the desktop into a folder could stay on the desktop as a ghost that cannot be deleted ("item no longer found"), because Explorer missed that the file had moved. DeskGrid now tells Explorer the desktop changed right after moving an item off it, so the ghost goes away by itself. If one is already there, press F5 on the desktop.
- The folder tile follows the size and position of the desktop's own icon pictures. It used to be a fixed 48 units at the top of the cell, which did not match a computer whose desktop icon size or spacing differs (seen on a desktop PC after developing on a laptop).

### How to use

- Settings → **NotiLand** → tick **Show NotiLand**, then **Open NotiLand settings…** to set what it shows and how it looks. Its tray icon and data (`%LOCALAPPDATA%\BrimDeck`) are its own.

## 0.7.3

### Fixed

- Settings → NotiLand → AI usage now shows whether **Read plan usage from Anthropic** actually has a usable Claude Code sign-in on this computer (**Signed in** / **No usable sign-in**), instead of silently staying an estimate when you turn it on with no sign-in. The automatic limit (highest earlier block) and the raw token count were never meant to replace that real percentage; this makes it obvious when you are only getting the estimate and what fixes it.

## 0.7.2

### Changed

- The top-edge trigger (resting the pointer at the top centre of the screen opens the list of all folders) is **off by default** now, and everyone starts with it off again. It opened the folders by accident when the pointer went to a browser's tabs or a window's title bar at the top centre. Turn it on in Settings → NotiLand → *Open the folders when the pointer rests at the top edge*. When on, it only reacts to the very edge of the screen (2 DIP instead of 8), needs the pointer to rest still for a full second (it was half a second), and ignores a pointer with a mouse button held.
- Files put on NotiLand's shelf are **copied** into a folder of DeskGrid's own (`%LOCALAPPDATA%\DeskGrid\shelf`), wherever they come from, so a file from a USB stick or a network share is still there after the stick is pulled. A copy is deleted when it is used: once it is dragged out and taken, when removed with the cross, with `Clear`, when the shelf is full and it is the oldest, and at exit (anything left by a crash goes at the next start). Dragged onto a folder widget or a folder panel it moves in (no shortcut to a copy that is about to go); dragged to Explorer it is copied or moved, never linked. The original is left where it was.

### How to use

- Click NotiLand to open its card, then drag files onto the card (or hold them over the top edge) to park them on the **Shelf**, from anywhere, a USB stick included. Drag them out onto a folder, a widget or Explorer when you need them; a copy disappears from the shelf once it is used, or when you click its cross or **Clear**.

### Fixed

- Widgets line up with the desktop icons beside them: the tile and the name sat about 12 px and 23 px lower than an icon's picture and name.

## 0.7.1

### Added

- **Plan usage from Anthropic** (off by default): Settings → NotiLand → AI usage → **Read plan usage from Anthropic**. DeskGrid reads the sign-in Claude Code keeps on this computer and asks Anthropic every few minutes for your plan's 5-hour and weekly usage, the numbers Claude's own usage page shows. The README's Privacy section lists it.

### How to use

- Settings → **NotiLand** → tick **AI usage**, then **Read plan usage from Anthropic**. It needs Claude Code signed in on this computer (run `claude` in a terminal and sign in once). Without it the usage stays an estimate, shown with `≈`.

### Changed

- The AI usage line says `≈` while it is only an estimate counted from Claude Code's logs. Cache reads are no longer counted (they were 10 to 20 times the rest, so a limit you set in tokens read as hundreds of percent).

### Fixed

- NotiLand no longer jumps to another place when the foreground flickers (Alt+Tab, the task view, a window closing). A new situation (desktop, window, maximized, full screen) counts only after it has stayed for about a second, and not while the pointer is on NotiLand, its card is open or it is being dragged.
- A button on the pill acts only on a click that began and ended on it. Releasing on Play/Pause after pressing elsewhere (or while the buttons slid under the pointer as the pill opened) no longer pauses the song, and Play/Pause now sends pause or play by what the player is doing at that moment.
- Clicking NotiLand opens the card even when the pill changes under the pointer between the press and the release.
- The cover of a song follows the player when it changes under the same title: browsers first offer their own logo and the video's picture a moment later, and the logo used to stay.
## 0.7.0

### Added

- **The island**: a small black pill at the top centre of the screen that shows a line of text for a few seconds and
  then goes. Scripts and AI agents send it lines with `DeskGrid.exe --ctl island show`. Several lines wait their turn.
  Point at it and it opens to show all of the text (and waits while you read); it hides while a full-screen game or
  video is up. Clicking a song or timer opens the NotiLand card with a smooth entrance animation, album-cover-tinted
  progress bars that you can click to seek, and a file shelf with one-drag batch export (`All (N)`) and `Clear`. It is
  off until you turn it on.
- **Automatic daily backup and one-click restore**: Settings → General → **Backup** can automatically back up all
  settings once a day at your chosen hour and keep up to 30 days of backups (older ones are deleted automatically).
  Clicking **Restore** opens a menu to restore any saved day's backup in one click, or browse for a backup file.

### How to use

- Settings → **Shortcuts** → tick **Show the island at the top of the screen**. To send it lines, also tick **Allow control from scripts and AI agents**.
- Send a line: `DeskGrid.exe --ctl island show "Build finished" 10` (the number is how many seconds, 5 if left out).
  `DeskGrid.exe --ctl island hide` takes it away.
- Point at the pill to read all of the text; click it to dismiss it.
- Settings → **General** → **Backup**: tick **Automatic daily backup**, pick the daily backup hour and how many days to keep (up to 30), or click **Restore** and choose a date from the menu.

### Changed

- NotiLand opens and closes more smoothly: the pill grows along a softer curve, no longer flutters when the pointer grazes its edge, and the card now grows out of the pill (and leaves toward it) instead of just appearing.
- Settings → NotiLand → **What the card shows**: tick the parts the card opens with (music and video, AI usage, system, folders, shelf) and move them up or down.
- AI usage can show Claude's own numbers (the 5-hour and weekly percentages and when they reset) instead of a guess counted from logs: Settings → NotiLand → AI usage → **Connect to Claude Code…**. It needs a Claude.ai Pro, Max or Ultra plan, and the numbers appear after Claude Code's next response.
- DeskGrid now finds which AI tools are on the computer (Claude Code, Codex, Antigravity, Gemini CLI, Cursor, GitHub Copilot) and lists them under Settings → NotiLand → AI usage. Tools that keep their plan usage on disk (Claude Code, Codex) get their own bar on the card; the others are listed as found.
- NotiLand's animations are shorter and lighter: the pill and the card open and close faster, only the side of the pill that changes is animated, a new line is not faded in again when the pill is already up, and a scrolling line runs at 30 frames a second.

### Fixed

- The song's progress bar on the card now has a dot at the current position: take it and drag to move the song (dragging anywhere on the bar works too).
- NotiLand does not tell about a song while its page is in front: a music app such as Apple Music, or the browser tab that plays it (YouTube and the like). It comes back when you leave that page.
- The card no longer closes while a file is being carried to the shelf (from Explorer, or out of the shelf), however long the trip takes, and an Esc that cancels such a drag does not close it either.
- While the card is up, its pill stays at medium size (as under the pointer) instead of shrinking to the smallest, and a line that ended does not take the pill away from it.
- Clicking NotiLand opens the card more reliably: a click that moves a few pixels is no longer taken for a drag, the second of two quick clicks is no longer ignored, and a click on a rotating info line (such as AI usage) opens the card instead of dismissing the line.
- Clicking a file's remove button on the shelf removes it without opening it.
- Folder deletion across drives handles read-only files cleanly.
- Automatic backup date parsing strictly matches backup file names, and the backup days dropdown stops at 30 days.
- Folder item hover and focus highlight corner radius matches the folder panel frame (8 px).
- A file dragged off the shelf and let go over the card again stays on the shelf (it used to vanish from it).
- A broken time in Codex's or Claude Code's usage data no longer stops the whole AI usage reading; connecting to Claude Code keeps the status line's other settings (such as padding) and no longer fails on an unusual settings file.
- A song hidden because its page is in front comes back reliably when you leave that page, and a song announcement hidden that way still ends on time.
- On the card, letting go of a song's bar after the song changed no longer leaves the bar holding the mouse, and moving one song's bar no longer freezes the other players' bars for a moment.
- The mouse wheel over a list inside a Settings page scrolls that list instead of the page; the usage line shows "1h 00m" instead of "60m"; a few rare crashes in NotiLand's player lookup and microphone/camera watcher are gone.
- NotiLand also hides a song while Windows' own Media Player (or another Store app) plays it in front, and after you move a song on the card its time and bar show the new place at once, even with players such as Media Player that do not report it.
- When Windows is out of drawing resources, dragging an item in a folder no longer crashes DeskGrid: the item is dragged without its flying icon and shows in its slot again afterwards.
- Picking a day under Settings → General → **Restore** now asks before it replaces your settings (a slip used to replace them at once); after changing the root folder, NotiLand no longer says items were added to a folder of the new root that only has the same name as one of the old.
- Moving a folder that holds a junction or folder link to another drive (dragging it in from the desktop, or deleting a folder to the desktop) no longer deletes the files of the folder that link points to.
- Turning NotiLand off while you set one of its places gives back the windows that were minimized for it.
- An automatic backup that was cut off while being written no longer stops that day's backup or shows up under Restore; a file kept open in Downloads no longer keeps NotiLand checking it forever; turning off downloads, Bluetooth or the top edge no longer lets one last line or folder list through.

## 0.6.2

### Changed

- Anything dragged from the desktop onto a folder is now moved in: files, programs and folders as well as shortcuts.
  It used to get a shortcut, so it showed on the desktop and in the folder. The setting (now **Items dragged in from
  the desktop**: **Move in** / **Keep on desktop**) covers all of them. Things dragged from anywhere else still get a
  shortcut and stay where they are.
- A folder inside a DeskGrid folder is shown as an item; clicking it opens it in Explorer.
- Shorter texts throughout: settings, dialogs and messages. The edge shading slider is dimmed when it does not apply (only Liquid glass has an edge).

### How to use

- Drag anything from the desktop onto a folder: it moves in and leaves the desktop. To keep the desktop as it is, choose **Keep on desktop** in Settings → General → **Items dragged in from the desktop**.

### Fixed

- Settings → Panel: dragging the opacity or edge shading slider with Liquid glass could freeze DeskGrid, and on a
  slower computer the whole desktop with it. The preview is now drawn in the background, and Liquid glass is
  drawn several times faster.

## 0.6.1

### Added

- Settings → Panel shows a small preview of a folder's panel that follows every change (columns, background, opacity,
  edge shading, color), so a look can be judged before it is used.
- Settings has **Apply** and **Done** instead of OK and Cancel. Apply keeps the changes and leaves the window open, so
  a folder can be opened to see the result. If something was changed and not applied, Done asks **Save the current
  settings?**.

### How to use

- Settings → **Panel**: change the background, then look at the preview above the options.
- Press **Apply** to use the settings and keep the window open; press **Done** when finished (it asks first if
  anything is not applied).

## 0.6.0

### Added

- Settings can be exported to a file and imported back (Settings → General → **Settings backup**), e.g. to move them
  to another computer. The file is named `DeskGrid_yyyy_mm_dd_hh_mm.json` (the time of the export). It holds the
  folder positions, item order, shortcuts, panel look and every other setting, not the folders' contents.

### How to use

- Settings → General → **Settings backup** → **Export…** to save the file, **Import…** to load one. An import asks
  first, then replaces the current settings and rebuilds the widgets.

## 0.5.3

### Fixed

- An update download that stalled (the connection stayed open but no data came) left the update window stuck for good.
  It now gives up after a minute without data and shows the error, so the buttons come back.
- A corrupt `config.json` that could not be moved aside (locked or read-only) stopped DeskGrid from starting. It now
  starts with the default settings.
- A root folder path Windows cannot read no longer raises an error when chosen; DeskGrid says it cannot use it.
- An icon or folder preview that failed to load once stayed blank until the file changed. It is tried again now.
- The 0.5.2 release was never published (its build failed); everything listed under 0.5.2 below is in this release.

### How to use

- Nothing to set up: these are fixes. The new 0.5.2 features (Liquid glass, edge shading, the paged Settings window)
  are explained under 0.5.2 below, and Settings → **Change log…** shows this list again later.

## 0.5.2

### Added

- The panel background's opacity can now be set for the system, dark and light styles too, not only a custom color.
  (The system style is blurred glass, which has no opacity: choosing a value turns it into a plain translucent color.)
- A new panel background, **Liquid glass**, in the spirit of iOS: the screen behind the panel shows through, lightly
  blurred and bent at the rim like curved glass, with a bright edge. The text turns dark or white to suit what the
  glass shows. Its code is adapted from two MIT-licensed projects, credited under "Third-party notices" in the README
  and linked from Settings.
- Liquid glass has an **Edge shading** slider: how dark the glass edge gets on the side away from the light. Set it to
  0% for a clean bright edge only.
- The Settings window now has a list of pages on the left (**General**, **Panel**, **Shortcuts and control**,
  **About**); click one to see its settings on the right.

### How to use

- Settings → **Panel background**, pick a style, then move the **Opacity** slider. Lower is more see-through.
- **Edge shading:** Settings → **Panel** → choose **Liquid glass**, then move **Edge shading**. 0% removes the dark
  edge, 100% is the strongest.
- **Finding a setting:** pick a page in the list on the left of Settings. Folder, drop and delete rules and the language
  are under **General**, the panel's columns, position and look under **Panel**, taskbar button, keyboard shortcuts and
  script control under **Shortcuts and control**, updates and links under **About**.
- **Liquid glass:** Settings → **Panel background** → **Liquid glass**, then **OK**. The **Opacity** slider is how
  frosted it is: lower is clearer, higher is easier to read over a busy wallpaper.

## 0.5.1

### Added

- After DeskGrid has been updated, a window shows what is new since the version you used last, with a short "How to
  use" for each new feature. It follows the interface language.

### How to use

- **After an update** the window opens by itself; close it with **Close** or Esc.
- **See it again later:** Settings → **Change log…**.

## 0.5.0

### Added

- Keyboard shortcuts that work from any program. **Ctrl+M** opens the list of all folders (press it again to close
  it), and any folder, **New folder…** and **Settings** can have a shortcut of your own under Settings → **Keyboard
  shortcuts** → **Customize…**. Click a box and press the keys; Backspace clears it.
- Scripts and AI agents can drive DeskGrid with `DeskGrid.exe --ctl <command>`: list, create, rename and fill folders,
  reorder items, open and close panels. Off until **Allow control from scripts and AI agents** is ticked in Settings;
  only programs of your own Windows account can use it, and nothing can be deleted this way.

### How to use

- **Open all folders from anywhere:** press **Ctrl+M**. Press it again to close.
- **Your own shortcut:** Settings → **Keyboard shortcuts** → **Customize…**, click a box and press the keys. Backspace
  clears it. A folder can have its own shortcut too.
- **Scripts and AI agents:** tick **Allow control from scripts and AI agents** in Settings, then run
  `DeskGrid.exe --ctl help` to see the commands.

## 0.4.3

### Fixed

- In Settings, the "Privacy" link ran under the "Change log…" button since the project's address got longer. The
  GitHub link now shows only `RT-Next-Energy-Systems/DeskGrid` (the full address is in its tooltip), and the links wrap
  instead of overlapping when they don't fit.

## 0.4.2

### Changed

- The project moved to `github.com/RT-Next-Energy-Systems/DeskGrid`: the update check, the GitHub link and the privacy
  link in Settings now go there. (Older versions keep working through GitHub's redirect from the old address.)

### Fixed

- Dropping a whole drive (such as `D:\`) on a folder put a shortcut called `.lnk` at the root of that drive; it now
  goes into the folder, named after the drive.
- Deleting a folder that has subfolders, with its items set to move to the desktop, failed when the root folder is on
  another drive than the desktop.
- The right-click menu of the folders on the desktop stayed in the old language after the language was changed in
  Settings.
- On a desktop so full that only cells under icons were left, a folder could be put on top of another folder.
- When something dropped on an open folder had nothing that could be added, the gap opened for it stayed.
- An update whose download did not match its checksum showed a "file in use" error instead of saying so.
- Wrapped lines of the Chinese change log were joined with a stray space.
- Changing the root folder in Settings while DeskGrid was still waiting for the desktop at sign-in put the folders up
  twice.
- A taskbar button clicked while DeskGrid was still starting at sign-in opens its folder once the folders are read,
  instead of not at all.
- "Browse…" in Settings no longer fails when the root folder box holds a path with invalid characters.
- In Settings, the "Change log…" button lines up with "Check for updates" above it.

## 0.4.1

### Fixed

- The color picker, the folder picker and the buttons of Windows message boxes now use the language chosen in
  DeskGrid's settings instead of Windows' language.

## 0.4.0

### Added

- Language setting: DeskGrid can be set to English or Traditional Chinese, or follow Windows. It applies as soon as
  the settings are accepted, and the change log and the privacy link follow it.
- "Where a folder opens" setting: a folder can open in the middle of the screen instead of next to its icon. Every
  folder then opens in the same place at the same size, which depends on the number of columns.
- Arrow keys, Home and End move through the items of an open folder; Enter opens the one that has the keyboard.
- Pressing Esc while dragging a widget puts it back where it was.
- The tray menu and the menu of a folder item follow the system's dark mode.
- The change log window shows a Traditional Chinese change log on a Chinese interface.
- A shortcut's icon is redrawn as soon as the program it points to is updated, not only when something in the
  folder changes.
- The rubber band drawn on the desktop also selects the folders it touches; dragging one selected folder moves all of
  them. (Ctrl+A still selects only Explorer's icons.)
- Files can be dropped on a folder in the all-folders list.
- Folders are hidden while Windows' "Show desktop icons" is off, and come back with the icons.
- Apps can be dragged from the Start menu onto a folder (its widget, its open panel, or its entry in the all-folders
  list); they become shortcuts.

### Fixed

- A folder opened from a taskbar button opens where the button was clicked, even if the mouse has moved on by the time
  DeskGrid has started.
- A folder opened from a taskbar button now sits right above the taskbar, instead of floating higher or lower
  depending on where in the button the mouse was.
- A taskbar button pinned for a folder that was deleted now opens the list of all folders instead of doing nothing.
- If the root folder is deleted and made again while DeskGrid runs, DeskGrid notices changes in it again without a restart.
- Widgets snap to the desktop's cells again after the taskbar, the desktop icon spacing or the display scaling
  changed.
- A folder opened by a drag from another app (which Windows won't give the keyboard) closes with Esc once something
  was dropped into it.

## 0.3.5

### Fixed

- A widget no longer loses its saved place when one of its shortcuts is deleted or locked while DeskGrid reads
  the folder.
- Shortcut icons are redrawn when the program they point to is updated.
- DeskGrid no longer freezes for a while when a shortcut points to a network share that is offline.
- DeskGrid starts even if `config.json` is briefly locked by a virus scanner or sync client.
- An error in a single action (a drag, a menu command, a timer) is logged and DeskGrid keeps running, instead of
  all widgets disappearing from the desktop.
- The update dialog shows an unexpected failure instead of leaving its buttons disabled, and a failed download no
  longer leaves a partial file behind.
- Widgets are placed at sign-in even if reading the desktop's icons fails.
- Taskbar buttons keep working after a rare communication error.

## 0.3.4

### Fixed

- Plugging in a second monitor, or arranging it to the left of (or above) the main one, no longer moves the
  widgets. They used to slide onto the other monitor, and their folders then opened on the main monitor. Widgets
  now stay exactly where they were put.
- A folder always opens next to its widget, on the same monitor as the widget, also when the monitors have
  different scaling.
- If a monitor is unplugged, its widgets wait on the nearest monitor and go back to their places when it returns.
  Their saved places are no longer overwritten.

## 0.3.3

### Added

- Settings: **Check for updates when DeskGrid starts** (on by default). Untick it and DeskGrid never contacts GitHub
  on its own; **Check for updates** still works when you press it.
- Settings: a **Privacy** link next to the GitHub address opens the Privacy section of the README (the Traditional
  Chinese one on a Chinese Windows).
- The README (English and Traditional Chinese) has a **Privacy** section, which lists everything DeskGrid sends over
  the network, and a **Code signing policy** section.

### Fixed

- A folder that opened because something was held over it during a drag now closes again when the drag moves away
  or ends somewhere else. Before, it stayed open and couldn't be closed by clicking elsewhere.
- An item dragged out of a folder can be put back: hold it over that folder's widget and the folder opens again.
- The whole cell of a widget takes drops (and clicks), not just the picture and the name. An icon let go just next
  to the picture no longer lands on the desktop under the widget.
- Holding a drag over a widget opens its folder reliably; moving across the widget no longer restarts the wait.
- A widget being dragged moves above the other widgets, not under them.
- Dragging an item out of a folder closes the folder after a moment instead of at once, so crossing from the folder
  to its widget doesn't close it.
- A folder low on the screen no longer flickers wildly while something is dragged into it: making room for the new
  item used to move it to another side of its widget and back, many times a second. A folder now stays on the side
  it opened on.
- A folder opened by a drag no longer gets taken over by the widget it covers.
- No more error lines in the log from shell menu handlers declining a menu message.

### Changed

- The exe's product version is now exactly the release version (`0.3.3`, not `0.3.3+<commit>`), so every build of a
  release reports the same version, as code-signing services require.

## 0.3.2

### Added

- Settings: **Open** next to the root folder, a **Taskbar** card with **Pin all folders to taskbar…**, the GitHub
  address, and a **Change log…** window that lists what changed in each version (built into the exe).

### Changed

- **Open root folder** and **Pin all folders to taskbar…** moved from the tray menu to Settings. The tray menu keeps
  New folder, Settings, Start with Windows and Exit.
- The settings window scrolls if the screen is too short for all of it.

## 0.3.1

### Added

- Right-click an item in an open folder for the menu Explorer shows for it (Open, Send to, Cut, Copy, Delete,
  Properties and whatever other apps add; hold Shift for the extra commands). Rename is not in it.
- **New folder…** in the right-click menu of a folder and of the name bar of an open folder.
- **Delete folder…** from the name bar of an open folder and from a folder's entry in the all-folders list, so a
  folder can be deleted without going back to the desktop or the root folder.
- Drag a folder onto the Recycle Bin icon on the desktop to delete it. The folder goes back to where it was and the
  usual "Delete folder…" question is asked, so nothing is lost by a slip.
- The first run asks whether DeskGrid should start with Windows. If Windows refuses to change the start-up entry, the
  tray menu now says so.

### Changed

- Start with Windows: the entry now carries `--autostart` (older entries are updated on the next start). A start at
  sign-in waits, up to 45 seconds, until Explorer has laid out the desktop icons before the folders are placed.

### Fixed

- A start at sign-in, before Explorer had put the desktop icons in place, could snap the folders onto the fallback grid
  and save those positions over the real ones. Without the icon grid, folders now stay where they were.
- Deleting a folder left its taskbar shortcut and icon in `%APPDATA%\DeskGrid\Pins`.

### Documentation

- README: what to do when Windows says "An Application Control policy has blocked this file" (Smart App Control).

## 0.3.0

### Added

- Folder names get a dark halo while the wallpaper behind them is light, so white names stay readable.
- Delete a folder from the desktop: right-click it → **Delete folder…**. The folder goes to the Recycle Bin. A new
  setting decides what happens to the items inside: they are moved to the desktop first (the default), or deleted
  along with the folder.
- Drag a desktop icon onto another one and the two become a new folder, which takes the place of the icon dropped
  on. Folders on the desktop are left alone: dropping onto one still moves the file into it.

### Removed

- The "new folder" tile on the desktop. New folders come from the tray menu or from dropping one desktop icon on
  another; dropping files on the tile to make a folder for them is gone with it.

### Fixed

- Dragging a folder on the desktop while its panel was open left the panel behind; it now closes.

## 0.2.5

### Added

- A desktop icon that lands on a widget (Windows places a new shortcut in what it thinks is a free cell, or an icon is
  dragged there) moves to the nearest free cell. With "Auto arrange icons" on, icons can't be moved, so the widget
  moves instead.

### Fixed

- A panel opened near the bottom of the screen cut off the names in its last row when it grew, for example when a
  folder with more items was opened from the folder list.
- The taskbar shortcut to all folders couldn't be pinned while (or after) the shortcut from 0.2.2 and earlier was
  pinned; it now has its own AppUserModelID.
- Dragging an item in a panel is smoother: the drag ghost no longer waits for a WPF frame each time it moves.

## 0.2.4

### Changed

- The new folder dialog and the update dialog have the same Fluent look as the settings window: they follow the
  system light or dark mode and group their content into a rounded card.

## 0.2.3

### Changed

- The settings window uses the Fluent look: settings are grouped into rounded cards, and the window follows the system
  light or dark mode.

### Fixed

- The taskbar shortcut to all folders is now named "DeskGrid" in every language; a shortcut left under the old name
  is removed when it is created again. A taskbar pin made from the old shortcut keeps its name: unpin it and pin the
  new one.
- The plus on the "new folder" tile on the desktop sat off the tile's center.
- Switching from the folder list to a folder with more items, or a folder gaining items while its panel was open,
  didn't resize the panel.
- Clicking a taskbar pin while its panel was open reopened the panel instead of closing it.
- After a drag in a panel, a rescan held back during the drag could undo the new order.
- Dropping something into a panel that couldn't be added left the gap opened for it.

## 0.2.2

### Changed

- The Update button now does the whole update in one dialog and shows its status live: the download with percent and
  size, then verifying the checksum, then installing and restarting. If the swap fails, the error is shown in the
  dialog and the update can be retried, instead of opening the release page.

## 0.2.1

### Fixed

- Shortcuts to Microsoft Store apps (for example Slack and WhatsApp) showed a blank document icon in widget previews
  and panels. These shortcuts have no file path, only an AppUserModelID, so the Shell was asked for the icon of the
  `.lnk` file itself. DeskGrid now looks the app up in the Apps folder and asks for its logo. Some packages have no logo
  of the size the icon-only request wants (WhatsApp); for those the request is repeated without icon-only, which finds
  a smaller logo.
- Dragging a folder in the "all folders" panel lifted only its preview image, which looked enlarged and lost the
  translucent tile under it. The drag ghost now carries the tile with the same padding as the grid cell.

### Added

- The folders in the "all folders" panel (opened from the taskbar) can be rearranged by dragging, with the same sliding
  animation as items inside a folder. The order is saved in `config.json` as `folderOrder`, new folders go last, and a
  renamed folder keeps its place. Dragging a folder never moves or copies it on disk.

### Changed

- The title of the "all folders" panel is now "DeskGrid".
