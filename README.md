<div align="center">

<img src="src/FocusLock.App/Assets/FocusMood.ico" width="72" alt="Focus Mood logo" />

# Focus Mood

**A Windows focus app that locks your screen to one canvas until the timer runs out.**

Name a session, pick how long it lasts and choose the plans you want to work through.
Until the clock hits zero, your PC shows only the whiteboard — no other apps, no Alt+Tab,
no Task Manager. When it's done, lay your work out on A4 pages and export a PDF.

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![UI](https://img.shields.io/badge/UI-WPF-5C2D91)
![License](https://img.shields.io/badge/license-MIT-green)
![Version](https://img.shields.io/badge/version-0.1.0-8fd18a)

</div>

---

> [!CAUTION]
> ## ⚠️ This app locks your computer
>
> While a session is running, **you cannot use your PC for anything else** until the timer ends:
>
> - The window goes fullscreen and stays on top; other monitors turn black.
> - Alt+Tab, the Windows key, Alt+F4, Ctrl+Shift+Esc and similar shortcuts are blocked.
> - **Task Manager is disabled** for the session.
> - **Restarting does not end the session** — the app comes back at sign-in
>   (and, with the guard service, before the desktop even appears).
> - The only early way out is the **Emergency exit**, where you must type a long random code exactly.
>
> **Before you use it:**
>
> 1. Read **[docs/RECOVERY.md](docs/RECOVERY.md)** so you know how to get out if something goes wrong
>    (Safe Mode + `--cleanup`, or WinRE).
> 2. Try a **30-second session** first, ideally inside a virtual machine.
> 3. Save your work in other apps and close anything you need — you won't be able to reach it.
> 4. Don't start a session on a shared or work computer that someone else depends on.
>
> This software is provided **as is, without warranty** (see [LICENSE](LICENSE)).
> You use it at your own risk; the author is not responsible for lost work or lost access.

## Features

### 🔒 Focus lock
- Fullscreen, topmost window; every other monitor is blacked out.
- Swallows escape shortcuts: Windows keys, Alt+Tab, Alt+Esc, Ctrl+Esc, Alt+F4, Alt+Space, Ctrl+Shift+Esc.
- Turns Task Manager off for the session (the one thing Ctrl+Alt+Del would reach).
- A foreground watchdog pulls the window back if anything else grabs focus.
- **Survives restarts** — a logon entry brings the session back, and the optional
  **guard service** has it up before the desktop even appears.

### 🛟 Safety first
- **Emergency exit**: type a long random code shown on screen to leave early.
- Hard ceilings: no session can run longer than 4 hours, and a lock that overruns its plan
  is forced open after a grace period — a bug can never hold the screen forever.
- `--cleanup` switch and uninstall scripts work from Safe Mode or WinRE.
- The guard service only ever *starts* the app; it never blocks anything itself, and it
  backs off if the app crash-loops.

### 🧠 Whiteboard
- One infinite canvas per plan in the session.
- Frames, text, sticky notes, shapes, tables and plan prompts.
- Freehand pen strokes and connectors with route and arrow styles.
- Marquee selection, snapping, duplicate, undo / redo, colour palettes.
- Autosaves every second.

### 📄 Arrange pages & PDF export
- **Extract tool**: box any region of a canvas to put it on the pages.
- Lay those regions out on A4 pages, like a document editor.
- Sections and text boxes that never overlap, swap places when dragged, and make room while you type.
- Exports to PDF with embedded fonts (saved to `Documents\Focus Mood`).

### 📚 Sessions
- Session history with progress, time range and plans; delete one or clear all.
- Finished sessions open read-only, and you can still extract and export from them.
- Session length from quick presets (25 min – 2 h) or an exact hours / minutes / seconds clock.
- Saved as JSON under `%LOCALAPPDATA%\FocusLock` — fully offline, no account, no telemetry.

## Install

1. Download `FocusMood-<version>-win-x64.zip` from the
   [**Releases**](../../releases) page and unzip it.
2. Run the installer script (no admin needed — installs to `%LOCALAPPDATA%\Programs\FocusLock`
   and adds a Start menu shortcut):

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
   ```

3. *(Optional, recommended)* From an **administrator** PowerShell, install the guard service
   so a session is back before the desktop is:

   ```powershell
   powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\FocusLock\scripts\install-service.ps1"
   ```

The release is self-contained — no .NET install required.

> Run it from a local disk. Started from a network or shared folder, the app works but a
> session can't resume after a restart (Windows runs logon entries before those folders exist).

### Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\FocusLock\scripts\uninstall.ps1"
```

If you installed the guard service, also run `uninstall-service.ps1` from an administrator window.

## Stuck in a lock?

In order of effort:

1. **Emergency exit** in the top bar → type the code exactly.
2. **Safe Mode** → `Win + R` → `"<install folder>\FocusLock.App.exe" --cleanup`
3. **WinRE command prompt** → remove the active session file by hand.

Full step-by-step instructions: **[docs/RECOVERY.md](docs/RECOVERY.md)**.

## Build from source

Requirements: Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone <this-repo-url>
cd <repo>
dotnet build FocusLock.sln
dotnet test  FocusLock.sln
```

Publish a self-contained build into `build\FocusLock` (scripts and recovery guide included):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```

> 💡 Test locking in a virtual machine first. A session length of `00:00:30` in Setup is
> enough to see the whole cycle.

## Project layout

```
src/
  FocusLock.Core/     Platform-free logic: sessions, clock, lock safety, board geometry,
                      document model, page layout, guard policy. Fully unit-tested.
  FocusLock.App/      WPF app (MVVM). Lock/ (kiosk, keyboard hook, watchdog), Guard/ (Windows
                      service), Board/ (whiteboard), Document/ + Export/ (pages and PDF).
tests/
  FocusLock.Core.Tests/   xUnit tests for Core (200+).
scripts/              build, install / uninstall, guard service, icon generator.
docs/RECOVERY.md      How to get out of a stuck lock.
```

One executable does two jobs: launched normally it's the app; launched by the Service Control
Manager with `--service` it's the guard. They can never be out of sync.

## Releasing

1. Bump `<Version>` in `src/FocusLock.App/FocusLock.App.csproj` and add an entry to
   [CHANGELOG.md](CHANGELOG.md).
2. Tag and push:

   ```bash
   git tag v0.1.0
   git push origin v0.1.0
   ```

The [release workflow](.github/workflows/release.yml) runs the tests, publishes a
self-contained win-x64 build and attaches the zip to a GitHub Release.

## License

[MIT](LICENSE) © 2026 Sifuddin Soad

## Credits

- Fonts: [Space Grotesk](https://github.com/floriankarsten/space-grotesk) and
  [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) (SIL Open Font License),
  [Material Symbols](https://github.com/google/material-design-icons) (Apache 2.0).
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet), [PDFsharp](https://github.com/empira/PDFsharp).
