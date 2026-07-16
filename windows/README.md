# STEM Windows preview

The Windows frontend is an isolated bridge that makes STEM useful while the
Objective-K Windows backend is completed. It does not replace or modify the
pure-Krypton macOS frontend (`stem.ks`), Linux/Wayland frontend (`stem_wl.ks`),
or portable terminal engine (`term.k`).

The current host provides:

- independent PowerShell/ConPTY tabs and nested split panes;
- PowerShell 7 by default, then Windows PowerShell, with Command Prompt only as
  a compatibility fallback;
- a VT grid with ANSI/256/truecolor, alternate-screen, cursor, resize, and
  bracketed-paste support;
- styled bounded scrollback with anchored live output, wheel/keyboard history
  navigation, selection, clipboard shortcuts, find highlighting/navigation, and
  a scroll-position indicator;
- an auto-created Linux-style stem.conf covering shell, grid, font, spacing,
  history, behavior, cursor, transparency, and the complete terminal palette;
- a Krypton-themed custom Windows frame around the shell; and
- a headless smoke test that exercises both the VT parser and a real PowerShell
  ConPTY round trip.

## Requirements

- Windows 10 version 1809 or newer (the first ConPTY release)
- .NET 8 SDK with the Windows Desktop workload

## Build and test

From the repository root:

```powershell
.\windows\build.ps1
.\windows\build.ps1 -Test
```

For iterative development:

```powershell
dotnet run --project .\windows\Stem.Windows\Stem.Windows.csproj
dotnet run --project .\windows\Stem.Windows.Smoke\Stem.Windows.Smoke.csproj
```

Set `STEM_SHELL` to override the default shell command line. Without an
override, STEM searches for `pwsh.exe`, then Windows PowerShell, and uses
`cmd.exe` only when neither PowerShell is available.

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
| Enter / Shift+Enter in find | Next / previous match |

## Configuration

STEM uses a plain Linux-style `key = value` file. `STEM_CONF` overrides the
location; otherwise Windows uses:

`%USERPROFILE%\.config\stem\stem.conf`

The file is created automatically. Existing legacy
`%USERPROFILE%\.config\stem\config` or `%APPDATA%\stem\config`
files are migrated without being overwritten. Visual and behavior settings
hot-reload when the STEM window regains focus. Restart STEM after changing the
shell, working directory, or `TERM`.

The repository's [stem.conf](../stem.conf) is the fully commented reference.
Core settings include:

```ini
theme = krypton
title = STEM
shell =
working_directory = ~
term = xterm-256color

cols = 117
rows = 30
font_family = MesloLGS Nerd Font, Cascadia Mono, Consolas
font_size = 13.5
padding = 10
line_spacing = 0

scrollback_lines = 10000
copy_on_select = false
confirm_close = false
bell = visual

cursor_style = bar
cursor_blink_ms = 530
cursor_color = #8B5CF6

background = #05070C
foreground = #D8DAD4
selection_background = #3A2A60
accent = #8B5CF6
opacity = 1.0

unfocused_pane_opacity = 0.82
split_divider_color = #8B5CF6
focus_follows_mouse = false
```

`opacity` accepts `0.2` through `1.0`. The temporary Windows host applies
it to the complete native window. `color0` through `color15` configure the
full ANSI palette; see the reference file for the Krypton defaults.

## Objective-K migration boundary

`ConPtySession.cs` is the Windows native boundary. `TerminalBuffer.cs` and
`TerminalView.cs` are a temporary managed renderer. They are deliberately kept
inside `windows/` so they can be replaced with Objective-K bindings and the
canonical `term.k` engine without changing the macOS or Linux paths.

