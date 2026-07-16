# stem — Krypton-first cross-platform terminal

STEM is a cross-platform terminal emulator built around Krypton. The canonical
terminal engine and the macOS and Linux window paths remain pure Krypton. The
Windows desktop preview is deliberately isolated under `windows/`: it uses a
small .NET/WPF + ConPTY bridge while Objective-K's Windows backend catches up,
and is designed to move back into Krypton without disturbing the other
platforms.

## Platform status

- **macOS:** working pure-Krypton Cocoa terminal with live zsh, truecolor,
  scrollback, selection, tabs, splits, mouse reporting, and full-screen TUIs.
- **Linux:** pure-Krypton Wayland/Hyprland frontend in `stem_wl.ks`, backed by
  the shared `term.k` grid and native PTY builtins.
- **Windows:** interactive ConPTY preview with a Krypton-themed desktop frame,
  PowerShell-first shell selection, VT/truecolor rendering, resize, keyboard
  input, and bracketed paste. The managed bridge is temporary and Windows-only.

```text
                         term.k — canonical Krypton terminal engine
                                      |
             +------------------------+------------------------+
             |                        |                        |
       stem.ks (macOS)          stem_wl.ks (Linux)       windows/ (Windows)
       Cocoa / Objective-K      Wayland / Krypton        WPF + ConPTY bridge
```

## Install (macOS, Apple Silicon)

```bash
brew tap t3m3d/krypton          # once
brew install stem           # the `stem` command -> run it to open a window
brew install --cask stem    # OR a clickable stem.app in /Applications
```

The cask app is ad-hoc signed (not notarized) — first launch, right-click → Open
(or `xattr -dr com.apple.quarantine /Applications/stem.app`).

Self-contained — no krypton runtime dependency. A
[JetBrainsMono Nerd Font](https://www.nerdfonts.com/) is recommended for the
powerline/icon glyphs (configurable in `~/.config/stem/config`).

## Build from source

### macOS

The build scripts are KryptScript (`.ks`, run with `kcc -r`), not shell.

```bash
kcc -r build_objk.ks   # -> dist/stem.app (pure Krypton, no Obj-C source)
open dist/stem.app
```

`build_objk.ks` compiles `stem.ks` straight to a Cocoa app through the
Objective-K FFI. The result links only `libobjc`, Foundation, and AppKit.

### Linux

```bash
kcc -r stem_wl.ks      # Wayland/Hyprland windowed frontend
kcc -r build_linux.ks  # static CLI terminal engine
```

### Windows

The usable desktop preview requires Windows 10 version 1809 or newer and the
.NET 8 SDK:

```powershell
.\windows\build.ps1 -Test
dotnet run --project .\windows\Stem.Windows\Stem.Windows.csproj
```

The older `kcc -r build_windows.ks` path remains the pure-Krypton Win32
prototype. It is preserved for Objective-K/backend work; the isolated preview
under `windows/` supplies the persistent ConPTY terminal in the meantime.
See `windows/README.md` for the migration boundary and shell override.

## Shipping (cask DMG)

```bash
kcc -r build_dmg.ks <version>          # → dist/stem-<version>.dmg
```

`build_dmg.ks` builds `stem.app` with the released `kcc` (objk, no Obj-C),
**fails closed** if the app isn't C-free, then makes the compressed DMG the cask
ships. With Homebrew `krypton` >= 2.4.0 on PATH it needs no config; otherwise
point `KRYPTON_ROOT` at a krypton 2.4.0 install. It prints the sha256 + the
`gh release upload` / `Casks/stem.rb` bump to finish a release.

## Engine (pure Krypton)

- **`term.k`** — incremental ANSI grid driver. Packed-string state (char +
  fg/bg attr planes + cursor + scrollback). Handles CUP/CUU/…/EL/ED (param-aware),
  ESC7/8 + `ESC[s/u` cursor save/restore, deferred wrap (auto-margin), SGR
  256-colour **and** truecolor (`48;2;r;g;b` → nearest xterm-256), multi-byte
  UTF-8 single-column cells, scroll with scrollback capture, OSC 0/2 title,
  find (`gridFind`).
- **`run.k -i`** — interactive bridge. Spawns the shell on a pty (native
  `ptyMaster`/`ptyForkExec`/`fdRead`/`fdWrite`/`fdSetNonblock`/`sleepUs`), feeds
  output through the grid, coalesces settled frames, and emits each as
  `SOH header SOH grid \f`. Reads keystrokes + control markers (resize / scroll /
  clear / find) back on stdin.
- **`pty.k`** — pty wrapper. **`ansi.k`** — standalone `stripAnsi`.
- **`stem.ks`** — the pure-Krypton Cocoa GUI on the **objk** FFI: `NSWindow`,
  custom `NSView` `drawRect:` (renders the `term.k` grid), `keyDown:` → pty,
  `NSTimer`/event-pump read loop, menus. No Obj-C source; see *Build from source*.

## Shortcuts

| Key | Action |
|---|---|
| ⌘C / ⌘V / ⌘A | copy selection / bracketed paste / select all |
| ⌘F · ⌘G · ⌘⇧G | find in scrollback · next · prev |
| ⌘K | clear screen + scrollback |
| ⌘N / ⌘T | new window / new tab |
| ⌘D / ⌘⇧D | split right / split down (File menu: also left / up) |
| ⌘W / ⌘⇧W / ⌘⌥W | close pane / close window / close all |
| ⌘Q | quit |
| ⌘+ / ⌘− / ⌘0 | font zoom in / out / reset |
| ⌘↑ / ⌘↓ | scrollback page up / down |
| ⌘Home / ⌘End | scrollback top / back to live |
| scroll wheel | scrollback |
| drag · 2-click · 3-click | select · word · line |
| ⌘-click · middle-click | open URL · paste |

## Config

`~/.config/stem/config` (auto-created, hot-reloads on window focus):

```ini
titlebar_light   = #2b2b2b      # dark grey in light mode, …
titlebar_dark    = #000000      # … black in dark mode (follows system appearance)
background_light = #2b2b2b
background_dark  = #000000
cursor_blink_ms  = 530          # 0 = steady
cursor_color     = #d8dad4
cursor_style     = bar          # bar | block | underline
font_family      = JetBrainsMono Nerd Font Mono
font_size        = 13
opacity          = 1.0          # 0.2–1.0, translucent bg (text stays opaque)
padding          = 6
line_spacing     = 0            # extra px between rows
bell             = visual       # visual | audible | off
copy_on_select   = false        # auto-copy selection; middle-click pastes
scrollback_lines = 2000
```

`⌘A` selects all · `⌘-click` opens underlined URLs · ⇧PageUp/Down also scrolls.

## Why this exists

Krypton's goal is to escape the C/C++/Qt runtime stack with compact native
programs. STEM demonstrates that with a portable Krypton terminal engine and
pure-Krypton macOS and Linux frontends. Windows currently keeps its temporary
ConPTY/WPF bridge behind a strict platform boundary so the application is useful
today and can be converted to Objective-K as Windows support matures.

## License

MIT — see [LICENSE](LICENSE).
