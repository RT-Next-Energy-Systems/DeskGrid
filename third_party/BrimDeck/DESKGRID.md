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
- **Added** `App.DeskGrid.cs`: when started with `--deskgrid <pid>` BrimDeck ends when that process ends, does not register itself
  to start with Windows (`StartupRegistration`), does not check for updates on its own (`StartBackgroundUpdates`), and a second
  start with `--quit` makes the running copy leave cleanly (`App.xaml.cs`, `OnStartup`).
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
