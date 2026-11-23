# DeskGrid memory: where it goes, what keeps it low

> This file and `memory.zh-TW.md` are twins (English / Traditional Chinese): change both together.

## Numbers (measured 2026-11-23, Windows 11, the published exe, NotiLand on, 8 folders)

| | Before | After |
|---|---|---|
| Private memory, idle | 195 MB | 129 MB |
| Private memory, a panel just closed | 231 MB | 141 MB |
| Working set at the start (peak) | 375 MB | 245 MB |
| Working set, idle | 51-62 MB | 16-55 MB |
| Working set, 2 s after a panel was opened and closed | 139-147 MB, kept for minutes | 123 MB, and 16 MB 27 s later |
| Click on a taskbar button to the panel's pixels | 51-66 ms | 56-70 ms (same) |

Nothing a user can do changed: `context/memory.md` is only about how the program holds memory. The exe is bigger (about 189 MB instead of 83 MB), which is the one cost.

## Where the memory is

- **The .NET heap is small** (9-17 MB after a clean-up). Do not look for savings there first.
- **Private memory** (about 125 MB idle) is mostly native: the runtime, JIT code, WPF and Direct3D's own heaps, and the pictures.
- **A compressed single-file bundle is unpacked into private memory when the program starts**, every assembly of it (the self-contained framework brings about 190 MB of them). That was 68 MB of private memory and 130 MB of working set at the start, and about 130 ms of every start.
- **A panel that has been opened takes about 90 MB of working set** (the renderer, the glass picture, the code it ran) and gives none of it back by itself.
- **There is no leak per panel**: 40 openings and closings, then a clean-up, ended 7 MB above where they began, with the handle count back to what it was.

## What keeps it low (do not undo)

1. **The single-file exe is not compressed** (`EnableCompressionInSingleFile` is `false` in `Properties/PublishProfiles/win-x64.pubxml`). Assemblies are mapped from the exe and only what runs is read. Turning compression on again costs about 68 MB of private memory and 130 ms of every start. If the download size ever matters more, trim what the bundle holds (unused framework assemblies) instead, and measure again.
2. **An idle clean-up** (`Diagnostics/MemoryTrim`, asked for by `App.RequestMemoryTrim`): 20 s after any use (a panel closed, the settings closed, the start) the heap is compacted, what it does not use goes back to Windows, and the process's pages leave its working set. It waits while a panel is open, while NotiLand is in use (`NotiLandHost.InUse`), while the settings window is open and while a mouse button is down (a drag). What is used again comes back page by page: the first panel after a clean-up opens as fast as any other (57-65 ms against 56-70 ms warm).
3. **The widgets draw in software** (`RenderMode = SoftwareOnly` on the desktop surfaces and the hidden message window), so no Direct3D device exists before a panel is first opened (see `CLAUDE.md`, "DesktopHost notes").
4. **Cached pictures stay in step with the folders**: `IconService.Retain` drops what is gone or changed at every refresh; a panel keeps the screen picture for its glass only while it is open (`PanelGlass`), and only the part around the panel (`CaptureMargin`), not the whole work area.
5. **NotiLand keeps its own, longer clean-up** (`MemoryMode` of BrimDeck, "Balanced" by default); the idle clean-up above is the host's and does not replace it.

## Rules for a change

- A change must not remove a feature or slow a click down. Measure both, before and after, as below.
- Do not hold on to a window, a bitmap or a task after the panel that made it has closed. Do not cache what is cheap to read again.
- A new background timer or watcher is a thread, a handle and some memory for as long as DeskGrid runs: make it stop when it has nothing to wait for.
- A new package or a new framework reference grows the bundle; the bundle is mapped, not unpacked, so it costs working set only for what runs, but it is download size.

## How to measure

Measured so far on one computer with one screen and one scaling; several screens and other scalings were not measured (the glass picture and the cell sizes depend on them). Measure them before relying on these numbers there.

1. Start the published build the way it really runs (`tools/run-latest.ps1`, or `explorer.exe DeskGrid.exe`), and wait 20 s and then 60 s.
2. `DeskGrid.exe --ctl memory` (needs "Allow control from scripts and AI agents") gives the managed heap, what the garbage collector holds, the working set and the private memory. Task Manager's "Memory" is the private working set, which is smaller than both.
3. Open and close a panel (`--ctl open Dev`, `--ctl close`), and read it again at 2 s and at 30 s: the working set must fall back by itself.
4. Open and close a panel 40 times, wait for the clean-up, and read it again: the private memory must end close to where it began, and the handle count (`Get-Process DeskGrid`) must too.
5. Check what each region of the process holds with `VirtualQueryEx` (private, mapped and image memory) when private memory has grown and the heap has not: the heap is small, so a growth is native.
6. Check the functions (a list of what was run is in `context/decisions.md`, 2026-11-23 (6)): every folder's panel, the list of all folders, a real click on the pinned taskbar button and on a desktop folder, creating, filling, renaming and deleting a folder, NotiLand's windows, and the log for errors. Then the speed: the time from a real click to the panel's first pixels, warm and after a clean-up.
