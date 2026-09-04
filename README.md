# <img src="logo.png" width="28"> Polyclicker

Auto-clicker and macro recorder for Windows. Record real mouse and keyboard
input and replay it on a loop, or run any number of independent clickers at
once, each with its own hotkey, interval, and target window. Single exe, no
installer.

<img src="docs/screenshot-light.png" width="100%" alt="Polyclicker, light theme">

<img src="docs/screenshot-dark.png" width="100%" alt="Polyclicker, dark theme">

## Features

- Macro recorder: capture a timeline of mouse and keyboard input, replay it
  looped, optionally positioned relative to the target window
- Macro editor: the take as a list of gestures — delete, retime, stretch,
  reorder, copy and paste steps, repoint clicks, insert new input, preview
  from any row; macros can be built in the editor from scratch
- Any number of clicker cards, each with its own hotkey (toggle or hold),
  interval, and settings
- Background clicking: a card tied to a window can click it without focusing
  it
- Per-window hotkeys: a gated card's key only fires in its window and types
  normally everywhere else
- Left/right/middle/X1/X2 click, a custom key, or a recorded macro
- Timing jitter (per click, or per step of a take), position jitter,
  per-click hold duration, or hold the input down for the whole run
- Start delay or a scheduled start time (HH:MM) per card
- Stop after N clicks or N seconds, or on any real input
- Profiles, kill switch, tray icon, dark/light theme
- Per-monitor DPI-aware; Ctrl+= / Ctrl+- / Ctrl+0 zoom the whole UI on top
  of that, for when Windows' scaling makes it larger than you want
- Export the current profile with the takes it uses as one zip (Settings);
  unpack it into `%APPDATA%\Polyclicker` on another machine to import
- One thread per running clicker, paced off the high-resolution performance
  counter and waiting on a kernel timer rather than spinning
- ~300 KB exe, no dependencies beyond the .NET Framework included in
  Windows, no installer or background service; settings are an INI file
- Update check in Settings: one request to the GitHub API when the window
  opens or on its button, nothing sent, nothing downloaded

## Download

Get `Polyclicker.exe` from the [latest release](../../releases/latest) and
run it from anywhere, or `Polyclicker-Setup-<version>.exe` for a per-user
install (no admin prompt) with a Start Menu entry and an uninstaller.
Settings are stored in `%APPDATA%\Polyclicker` either way (set the
`POLYCLICKER_DATA` environment variable to relocate them, e.g. for a
portable install); uninstalling leaves them in place. A session log
(`log.txt`) is written to the same folder.

The exe is unsigned, so SmartScreen or antivirus may warn on first run. If
you'd rather not trust a downloaded binary, build it yourself:

## Build

No SDK or NuGet needed; the compiler ships with the .NET Framework:

```powershell
.\build.ps1
```

or directly:

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize+
    /target:winexe /out:Polyclicker.exe *.cs
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
```

Requires .NET Framework 4.5 or later (included in Windows 8 and later).

The installer is built with [Inno Setup 6](https://jrsoftware.org/isinfo.php)
(`winget install JRSoftware.InnoSetup`):

```powershell
.\installer\make-installer.ps1
```

## Tests

```powershell
.\csharp\tests\run-tests.ps1
```

Compiles the sources with a test entry point and runs them against a
throwaway data folder. The checks inject real input (F13–F16, the X2
button, pointer moves) and take about ten seconds; leave the desktop alone
while they run. `-Repro` drives the macro editor with real keystrokes,
`-Shots` renders screenshots into `tests\bin`; with `POLYCLICKER_UISCALE=150`
in the environment they render at 150%, which is how the high-DPI layout is
checked on a 100% display. The same variable overrides the UI size setting
for the app itself. `pixdiff.ps1 before.png after.png` lists the pixels
that differ between two screenshots, for checking that a rendering change
drew the same picture. `run-perf.ps1` measures CPU per resize tick with a
twelve-card profile, and a running clicker and macro.

## License

[MIT](LICENSE)

## Disclosure

A large share of this code was written by an AI assistant, with a human
directing, reviewing, and testing it. I'm Sorry. Review the source before
trusting the binary. This program installs system-wide keyboard and mouse
hooks and synthesizes input; a bug could swallow keystrokes or leave a mouse
button logically held down. Avoid running it in situations where unintended
clicks could cause harm. Provided as-is, without warranty.
