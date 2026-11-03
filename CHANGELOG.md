# Changelog

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
