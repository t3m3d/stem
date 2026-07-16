# STEM for Windows

STEM is a Krypton-themed terminal emulator for Windows 10/11. The current
Windows host is an isolated bridge while the Objective-K Windows backend is
completed; it does not replace or modify the pure-Krypton macOS frontend
(`stem.ks`), Linux/Wayland frontend (`stem_wl.ks`), or portable terminal
engine (`term.k`).

## Current 0.1 feature set

- PowerShell 7 by default, with Windows PowerShell fallback and Command Prompt
  only as a compatibility fallback.
- Independent tabs, nested horizontal/vertical split panes, pane focus/zoom,
  and per-tab/per-pane right-click menus.
- PowerShell, custom command, WSL, and SSH profiles. Installed WSL
  distributions are discovered automatically.
- Session restoration for tabs, nested split layout, active panes, profiles,
  window position/size, and maximized state.
- VT/ConPTY terminal rendering with ANSI/256/truecolor, alternate screen,
  cursor modes, resize, bracketed paste, Unicode/Nerd Font glyphs, wide and
  combining characters.
- Bounded scrollback, search, selection, copy/paste, and keyboard navigation.
- Krypton Dark and Krypton Light application/terminal palettes. The default
  terminal surface is deep Krypton violet (`#160A2A`), not neutral black.
- Background-only transparency: text remains opaque. Windows 11 uses the
  system translucent backdrop at opacity values below 1.0.
- A native Krypton Settings GUI that generates the same portable
  Linux-style `stem.conf` users can edit directly.
- Self-contained, versioned Windows release ZIPs with SHA-256 checksums.

## Requirements

Running a packaged release requires Windows 10 version 1809 or newer.
The translucent backdrop is available on Windows 11; Windows 10 uses the
configured tint without the Windows 11 material.

Building from source requires the .NET 8 SDK with the Windows Desktop workload.

## Build, test, and publish

From the repository root:

```powershell
.\windows\build.ps1
.\windows\build.ps1 -Test
.\windows\publish.ps1 -Version 0.1.0 -Runtime win-x64
.\windows\package-msix.ps1 -Version 0.1.0.0
```

The publish command reruns the regression suite, creates a self-contained
single-executable package, and writes these artifacts under `dist/`:

- `stem-<version>-win-x64.zip`
- `stem-<version>-win-x64.zip.sha256`
- `stem-<four-part-version>-x64.msix`
- `stem-<four-part-version>-x64.msix.sha256`

The MSIX uses the Microsoft Store identity `t3m3d.StemTerminalforWindows`
and signs with a valid Current User code-signing certificate whose subject is
`CN=97613709-C254-4F66-AB6B-1EE4BA3D003F`. Pass `-Unsigned` only when creating
an upload artifact that the Store will sign during ingestion.

For iterative development:

```powershell
dotnet run --project .\windows\Stem.Windows\Stem.Windows.csproj
dotnet run --project .\windows\Stem.Windows.Smoke\Stem.Windows.Smoke.csproj
```

## Interaction

| Input | Action |
|---|---|
| Mouse wheel | Scroll history; sends arrow keys in an alternate-screen app |
| Shift+PageUp / Shift+PageDown | Scroll one terminal page |
| Ctrl+Shift+Home / Ctrl+Shift+End | Oldest history / return to live output |
| Drag / double-click / triple-click | Select cells / word / line |
| Ctrl+Shift+C / Ctrl+Shift+V | Copy selection / bracketed paste |
| Ctrl+Shift+A | Select the visible viewport |
| Ctrl+Shift+F | Find across scrollback and the live grid |
| Ctrl+Shift+T / Ctrl+Shift+W | New tab / close active pane (or its sole tab) |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Alt+Shift+Plus / Alt+Shift+Minus | Split active pane right / down |
| Alt+Arrow | Focus the pane in that direction |
| Alt+Shift+Enter | Zoom / restore the active pane |

The `+` button opens the default profile. The adjacent dropdown opens any
detected/saved profile and contains split, find, and Settings actions.

## Configuration

On Windows, Settings is the primary configuration surface. Choose
**Settings...** from the terminal dropdown, edit the GUI controls, and use the
**CONFIG CODE** tab to inspect or directly edit the generated portable config.

The default config path is:

`%USERPROFILE%\.config\stem\stem.conf`

`STEM_CONF` overrides that path. `STEM_SESSION` can override the session
JSON path for portable/test deployments. Visual and behavior settings
hot-reload when the window regains focus.

Core settings:

```ini
theme = krypton-dark             # krypton-dark | krypton-light
title = STEM
shell =
working_directory = ~
term = xterm-256color

font_family = MesloLGS Nerd Font, Cascadia Mono, Consolas
font_size = 13.5
padding = 10
line_spacing = 0
scrollback_lines = 10000

background = #160A2A
foreground = #F4EEFF
selection_background = #3D286B
accent = #8B5CF6
opacity = 1.0                    # background only; text remains opaque

restore_session = true
unfocused_pane_opacity = 0.82
split_divider_color = #8B5CF6
focus_follows_mouse = false
```

`opacity` accepts 0.2 through 1.0. `color0` through `color15`
configure the ANSI palette. Profiles use
`profile.<id>.name/kind/command/working_directory` keys.

Fatal startup/runtime failures are appended to:

`%USERPROFILE%\.config\stem\logs\stem-crash.log`

## Objective-K migration boundary

`ConPtySession.cs` is the Windows native boundary. `TerminalBuffer.cs` and
`TerminalView.cs` are a temporary managed renderer. They remain inside
`windows/` so they can be replaced with Objective-K bindings and the
canonical `term.k` engine without changing the macOS or Linux paths.
