# Handoff: Windows Krypton capabilities required to replace STEM's C#/WPF bridge

> Paste this file into the Krypton development chat as the implementation brief.

**Date:** 2026-07-16  
**From:** STEM Windows work  
**Repo:** `t3m3d/stem`  
**Target:** KryptScript + Objective-K/Win32 + native Krypton language/runtime  
**Current Windows target:** Windows 10 1809+ (`10.0.17763`), x86-64 first  

## Objective

Add the missing Windows capabilities to Krypton so STEM can replace the temporary
`.NET 8 + WPF + C#` host under `windows/` with a native Krypton application.

The final Windows application must:

- use the canonical `term.k` terminal engine;
- run a persistent PowerShell/WSL/SSH shell through ConPTY;
- preserve the existing Windows tabs, splits, settings, session restoration,
  Krypton themes, input, selection, scrollback, and Store packaging;
- build and package through KryptScript without requiring `cmd.exe`, Bash,
  Python, .NET, WPF, or a C/C++ compiler at runtime;
- keep the pure-Krypton macOS and Linux paths fully working.

## Non-negotiable compatibility rule

Do **not** rewrite or weaken these files to accommodate Windows:

- `stem.ks` — pure-Krypton macOS/Cocoa frontend
- `stem_wl.ks` — pure-Krypton Linux/Wayland frontend
- `term.k` — canonical shared terminal engine

Windows-specific code must live behind new Windows modules/adapters. Shared
terminal fixes may enter `term.k` only when covered by cross-platform tests.
The current C#/WPF implementation stays isolated until the native replacement
passes the complete acceptance checklist in this handoff.

## Known Krypton Windows blockers already demonstrated by `stem_win.ks`

- [ ] **Reliable module-level mutable state.** `stem_win.ks` currently routes
  all mutable values through `guiStateSet/guiStateGet` because module-level
  mutable `let` values have returned invalid data across function boundaries.
- [ ] **Binary-safe byte buffers.** KryptScript strings are NUL-terminated and
  cannot safely represent arbitrary PE, ConPTY, UTF-16, or pipe data.
- [ ] **A compiler PE subsystem option.** `kcc` cannot currently emit a Windows
  GUI subsystem binary directly. `build_windows.ks` patches the PE with
  external `od`, `dd`, and Bash.
- [ ] **Structured Windows process execution.** Windows `exec()` currently
  wraps text in `cmd /c`, causing quoting, redirection, and executable-path
  failures. STEM is PowerShell-first and must not depend on Command Prompt.
- [ ] **Real resize/event dispatch in `gui.k`.** The prototype lacks proper
  `WM_SIZE` dispatch and uses an oversized RichEdit plus timer-based docking.
- [ ] **A real persistent pseudo-terminal.** The prototype runs one command at
  a time through `exec()` and is not an interactive terminal. It cannot run
  Starship, full-screen TUIs, shell completion, or terminal protocols correctly.
- [ ] **Incremental asynchronous I/O.** ConPTY output must be read without
  blocking the Windows message loop and then marshalled safely to the UI thread.

## Source-to-native migration map

| Temporary Windows source | Responsibility today | Native Krypton destination |
|---|---|---|
| `ConPtySession.cs` | ConPTY allocation, process creation, pipes, resize, streaming I/O | Objective-K Win32/ConPTY module |
| `TerminalBuffer.cs` | Temporary duplicate VT parser and grid | Replace with canonical `term.k` |
| `TerminalView.cs` | Text rendering, cursor, selection, search, input, clipboard | Objective-K Windows renderer/input adapter |
| `MainWindow.xaml(.cs)` | Window chrome, tabs, nested splits, menus, pane focus/zoom | Objective-K native Windows UI/layout module |
| `SettingsWindow.xaml(.cs)` | Native settings GUI and `stem.conf` generation | Objective-K controls + shared Krypton config model |
| `StemSettings.cs` | Config parsing, defaults, validation, themes | Extend/reuse `config.k` with shared schema/validation |
| `TerminalProfile.cs` | PowerShell, WSL, SSH, custom profile discovery | Krypton Windows process/profile module |
| `SessionStore.cs` | Atomic JSON tab/split/window restoration | Krypton structured-data + atomic file module |
| `KryptonTheme.cs` / `DarkWindowTheme.cs` | Palette resources, DWM dark/light/transparency | Objective-K DWM/theme module |
| `App.xaml.cs` / `CrashReporter.cs` | Startup, global failures, diagnostic logs | Krypton Windows application/crash boundary |
| `windows/*.ps1` | Build, publish, MSIX, signing, validation | KryptScript Windows build/package tasks |

## P0 — language, ABI, and runtime foundation

These items block all serious Windows native work.

### 1. Binary-safe native data

- [ ] Add a byte-buffer type that preserves every value from `0x00` to `0xFF`.
- [ ] Support buffer allocation, length/capacity, indexed reads/writes, slicing,
  copying, zeroing, and conversion to/from strings with an explicit encoding.
- [ ] Support fixed-width signed and unsigned integers (`u8/u16/u32/u64`,
  `i8/i16/i32/i64`) and a pointer-sized integer/handle type.
- [ ] Support little-endian typed reads/writes for PE and Win32 structures.
- [ ] Keep embedded NUL bytes intact across file I/O and native calls.
- [ ] Add incremental UTF-8 decoding and explicit UTF-16LE conversion,
  including surrogate pairs and malformed-input replacement behavior.

**Acceptance test:** a Krypton test writes and rereads all 256 byte values,
passes a UTF-16LE environment block containing embedded NUL separators to a
Win32 API, and incrementally decodes a multibyte UTF-8 rune split across reads.

### 2. Complete Win64 FFI/ABI support

- [ ] Fixed-layout structs with correct size, alignment, nesting, and field offsets.
- [ ] Pointer-to-struct, pointer-to-buffer, pointer-to-pointer, and nullable pointers.
- [ ] Native callbacks/function pointers with stable lifetime.
- [ ] Win32 `BOOL`, `DWORD`, `HRESULT`, `HANDLE`, `HWND`, `HPCON`, `WPARAM`,
  `LPARAM`, `LRESULT`, and wide-string mappings.
- [ ] Correct Microsoft x64 calling convention, stack alignment, and return values.
- [ ] `GetLastError()` preservation immediately after a failing FFI call.
- [ ] Deterministic cleanup (`defer`, scoped owner, or equivalent) for handles,
  buffers, GDI/DWrite objects, pseudo consoles, pipes, and process/thread handles.
- [ ] Reliable mutable module/application state without the GUI state-table workaround.

**Acceptance test:** define and validate `COORD`, `SECURITY_ATTRIBUTES`,
`STARTUPINFOEXW`, `PROCESS_INFORMATION`, and `PROC_THREAD_ATTRIBUTE_LIST`
layouts, then launch a process through `CreateProcessW` without memory corruption.

### 3. Concurrency and Windows event-loop integration

- [ ] Native threads or a runtime task primitive for blocking pipe reads.
- [ ] Locks/atomics or message-passing for terminal buffer ownership.
- [ ] A safe UI-thread dispatch primitive (`PostMessage`, dispatcher queue, or equivalent).
- [ ] Timers with cancellation for cursor blink, visual bell, config reload, and coalesced frames.
- [ ] Clean cancellation, EOF, child-exit notification, and application shutdown.
- [ ] Exceptions/result values must cross task boundaries without silently terminating the process.

**Acceptance test:** stream at least 100 MB from a ConPTY child while the native
window remains responsive, resizable, and closable, with no lost final output.

### 4. Structured process API for Krypton and KryptScript

Add an API that does not assemble a shell command string:

```text
processSpawn(executable, argv[], environment{}, cwd, options)
processReadStdout(process, bytes)
processReadStderr(process, bytes)
processWriteStdin(process, bytes)
processWait(process, timeoutMs)
processKill(process, tree=true)
```

- [ ] Preserve arguments containing spaces, quotes, Unicode, and trailing backslashes.
- [ ] Allow inherited/overridden environment variables and working directory.
- [ ] Capture stdout/stderr as bytes, not NUL-terminated strings.
- [ ] Allow no-window and explicit Windows creation flags.
- [ ] Make PowerShell the preferred scripting shell when a shell is genuinely required.
- [ ] Remove the current implicit `cmd /c` behavior from structured execution paths.

**Acceptance test:** invoke an executable located under `C:\Program Files`, pass
Unicode and quoted arguments losslessly, capture UTF-8 and UTF-16 output, and
terminate a timed-out process tree.

### 5. Native Windows PE output

- [ ] Add `kcc --target windows-x86_64` and a direct GUI subsystem option such
  as `--subsystem windows`.
- [ ] Embed the application manifest, icon, version resource, product name,
  description, and publisher metadata into the PE.
- [ ] Add reproducible release/debug modes and symbols suitable for crash analysis.
- [ ] Add Windows ARM64 as a follow-up target without changing app source.
- [ ] Remove the `od/dd/bash` PE patch from `build_windows.ks` once the compiler
  emits the correct subsystem directly.

**Acceptance test:** Explorer launches the generated executable without a console
window; `dumpbin /headers` reports Windows GUI subsystem; resources and manifest
are embedded; the binary runs on a clean Windows 10 1809+ VM.

## P0 — Objective-K Windows native surface

### 6. Complete ConPTY binding

The Objective-K Windows layer must expose safe wrappers for:

- [ ] `CreatePipe`, pipe inheritance control, `ReadFile`, `WriteFile`, `CloseHandle`.
- [ ] `CreatePseudoConsole`, `ResizePseudoConsole`, `ClosePseudoConsole`.
- [ ] `InitializeProcThreadAttributeList`, `UpdateProcThreadAttribute`,
  `DeleteProcThreadAttributeList`.
- [ ] `CreateProcessW` with `EXTENDED_STARTUPINFO_PRESENT` and
  `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`.
- [ ] Process exit observation and orderly child/process-tree shutdown.
- [ ] Environment block creation with `TERM=xterm-256color`,
  `COLORTERM=truecolor`, and no inherited `NO_COLOR`.
- [ ] Explicit working directory and correct default to the user's profile.
- [ ] Resize propagation whenever the terminal grid changes.

**Acceptance test:** start PowerShell 7, render a complete Starship prompt, resize
the window, run `vim` or another alternate-screen TUI, paste multiline text with
bracketed paste, print truecolor, and exit without leaked handles.

### 7. Win32 application/window lifecycle

- [ ] Register window classes and run a stable Unicode `GetMessageW` loop.
- [ ] Dispatch `WM_CREATE`, `WM_DESTROY`, `WM_CLOSE`, `WM_SIZE`, `WM_DPICHANGED`,
  `WM_PAINT`, focus, activation, mouse, keyboard, timer, and command messages.
- [ ] Support multiple top-level windows without global-state collisions.
- [ ] Custom title bar hit testing, drag, minimize, maximize/restore, close, and
  resize borders, including maximized work-area behavior.
- [ ] Per-monitor DPI v2 awareness and layout scaling across monitors.
- [ ] Minimum size, saved placement, maximized restoration, and multi-monitor bounds validation.
- [ ] Windows 10 fallback behavior and Windows 11 rounded-corner/backdrop behavior.

**Acceptance test:** resize continuously at 100%, 150%, and 200% scaling; move
between monitors; maximize/restore; open two STEM windows; verify no white legacy
chrome, clipping, or stale layout.

### 8. DWM theme and transparency bindings

- [ ] `DwmSetWindowAttribute` and `DwmExtendFrameIntoClientArea` wrappers.
- [ ] Dark/light title-bar mode, rounded-corner preference, and system backdrop type.
- [ ] Background-only opacity: terminal text, cursor, borders, controls, and menus remain opaque.
- [ ] Krypton Dark and Krypton Light resource palettes with live updates.
- [ ] Consistent styling for popups, right-click menus, scrollbars, tooltips,
  settings dialogs, and system-facing application surfaces.

**Acceptance test:** switching theme updates the entire app without legacy light
controls; opacity reveals windows behind the background while glyphs remain fully opaque.

## P1 — renderer, text, and terminal core

### 9. Renderer-neutral terminal display API

Define a shared display-list contract produced from `term.k`, for example:

```text
beginFrame(rows, cols, background)
fillCells(row, startCol, count, color)
drawGlyphRun(row, startCol, cells, style)
drawDecoration(row, startCol, count, kind, color)
drawCursor(row, col, shape, color)
endFrame(dirtyRegions)
```

- [ ] Keep terminal state/parsing in `term.k`; platform frontends only render.
- [ ] Support dirty rows/regions so large logs do not repaint the whole grid.
- [ ] Preserve scroll position while new output enters scrollback.
- [ ] Coalesce output into frames without delaying interactive input.

### 10. Windows text renderer

DirectWrite + Direct2D is the preferred first native backend; Direct3D can be
added behind the same display contract for higher-performance effects/images.

- [ ] Hardware-accelerated drawing with device-loss recovery and software fallback.
- [ ] Monospace cell measurement that remains stable across DPI values.
- [ ] Font-family fallback lists and installed-font enumeration.
- [ ] Bold and underline now; extensible italic, faint, strike, and styled underline metadata.
- [ ] Unicode scalar values, combining marks, wide cells, continuation cells,
  supplementary-plane Nerd Font glyphs, emoji sequences, and font fallback.
- [ ] Optional ligatures/font features without breaking terminal cell positions.
- [ ] Opaque text over independently translucent background.
- [ ] Cursor bar/block/underline, configurable blink, selection/search highlights,
  pane dimming, split borders, and terminal scroll indicator.

**Acceptance test:** render the current Unicode smoke corpus, Nerd Font prompts,
CJK wide glyphs, combining accents, emoji, 16/256/truecolor, and 10,000+ lines
of scrollback without cell drift or visible full-frame flashing.

### 11. Bring `term.k` to Windows parity before deleting `TerminalBuffer.cs`

- [ ] Incremental UTF-8 input across arbitrary ConPTY chunk boundaries.
- [ ] CR/LF/BS/TAB/BEL and deferred right-margin wrapping.
- [ ] CSI cursor movement/positioning and save/restore.
- [ ] Erase display/line, including `CSI 3 J` scrollback clearing.
- [ ] Insert/delete/erase characters and insert/delete lines.
- [ ] Scroll regions, reverse index, scroll up/down.
- [ ] SGR 16-color, 256-color, truecolor foreground/background, bold,
  underline, inverse, and resets.
- [ ] Primary/alternate screen save and restore (`?1049`).
- [ ] Cursor visibility, auto-wrap, and bracketed-paste modes.
- [ ] OSC 0/2 title changes and terminal response callbacks (DA/DSR/window status).
- [ ] Bounded scrollback, resize, search coordinates, wide/combining-cell correctness.
- [ ] A renderer-facing cell/style API that does not expose packed-string internals.

**Acceptance test:** port the complete `Stem.Windows.Smoke` terminal corpus to a
shared Krypton test suite and run it unchanged on Windows, macOS, and Linux.

## P1 — Windows input and desktop UI

### 12. Keyboard, text, IME, mouse, and clipboard

- [ ] Separate physical key events from composed text input.
- [ ] Correct Ctrl+A–Z, Alt/Meta prefix, Shift+Tab, navigation, Insert/Delete,
  Page keys, F1–F12, and configurable shortcuts.
- [ ] UTF-16 surrogate handling, dead keys, AltGr, keyboard layouts, and IME composition.
- [ ] Mouse capture, drag selection, double-click word selection, triple-click line selection.
- [ ] Mouse wheel scrollback and alternate-screen arrow behavior.
- [ ] Windows clipboard Unicode text read/write with retry for temporary ownership contention.
- [ ] Bracketed-paste wrapping and newline normalization.
- [ ] Future-ready SGR mouse reporting and focus reporting.

### 13. Native controls and layout needed by the current STEM UI

- [ ] Horizontal tab strip with active state, close button, shell/profile title,
  overflow behavior, and new-tab action.
- [ ] Recursive horizontal/vertical split tree with adjustable divider, active
  pane border, directional focus, zoom/restore, and pane close/collapse.
- [ ] Themed popup/context menus with separators, submenus, shortcuts, scroll,
  and custom Krypton scrollbar styling.
- [ ] Search overlay with text input, previous/next, match count, and close.
- [ ] Settings window controls: tabs, scroll views, text boxes, editable combo
  boxes, checkboxes, sliders, buttons, labels, palette fields, and dynamic profile editors.
- [ ] Keyboard navigation, focus visuals, high-contrast fallback, screen-reader
  names/roles, and UI Automation providers.

**Acceptance test:** all current Windows keyboard shortcuts and pointer actions
work without WPF, including nested splits, popup scrolling, and keyboard-only Settings use.

### 14. Profiles and shell discovery

- [ ] Resolve PowerShell 7 (`pwsh.exe`) first, Windows PowerShell second, and
  Command Prompt only as an explicit compatibility fallback.
- [ ] Search `PATH` and known install locations without executing through a shell.
- [ ] Run `wsl.exe --list --quiet`, detect UTF-16LE/UTF-8 output, normalize NULs,
  deduplicate distributions, and create WSL profiles.
- [ ] Support configured PowerShell, WSL, SSH, and custom-command profiles.
- [ ] Preserve profile ID, name, kind, command argv, working directory, and default profile.

## P1 — configuration, persistence, and diagnostics

### 15. Shared `stem.conf` model

- [ ] Extend `config.k` to work with Windows home-directory resolution and
  `%USERPROFILE%\.config\stem\stem.conf` while preserving `$STEM_CONF` override.
- [ ] Parse comments, blank lines, inline comments, compatibility aliases,
  booleans, bounded integers/decimals, colors, and `profile.<id>.*` keys.
- [ ] Generate the same portable `.conf` from the Windows Settings GUI.
- [ ] Validate manual config-code edits before saving.
- [ ] Save atomically through a sibling temporary file plus replace/rename.
- [ ] Hot-reload safe visual settings; apply shell/layout changes only to new sessions when required.

### 16. Structured session storage

- [ ] Provide a small JSON reader/writer or an equally stable shared structured-data module.
- [ ] Preserve snake_case fields and a versioned schema.
- [ ] Persist window placement/state, tabs, recursive split trees, active tab,
  active pane, and profile IDs.
- [ ] Honor `$STEM_SESSION` and use atomic UTF-8-without-BOM writes.
- [ ] Reject corrupt/unknown versions safely and fall back to one default tab.

### 17. Crash and diagnostic boundary

- [ ] Catch startup, event-loop, renderer, ConPTY-reader, and callback failures.
- [ ] Append human-readable diagnostics to
  `%USERPROFILE%\.config\stem\logs\stem-crash.log`.
- [ ] Include stage, timestamp, OS/build, architecture, compiler/runtime version,
  exception/error code, and native stack/symbol information when available.
- [ ] Show a Krypton-themed fatal dialog only when the app cannot continue.
- [ ] Never let smoke tests display user-facing dialogs.

## P2 — KryptScript build, test, and Microsoft Store packaging

### 18. Native Windows build script support

- [ ] Replace `test`, `cp`, `bash`, `od`, and `dd` usage with portable KryptScript APIs.
- [ ] Add filesystem exists/copy/move/remove/create-directory, recursive enumeration,
  binary read/write, SHA-256, ZIP, and deterministic timestamp options.
- [ ] Use structured process execution for SDK tools with explicit argv arrays.
- [ ] Make `kcc -r build_windows.ks` work from PowerShell on a normal Windows checkout.
- [ ] Produce a self-contained executable with no .NET/WPF dependency.

### 19. Regression and integration tests

- [ ] Port the current C# smoke tests into Krypton test fixtures.
- [ ] Add byte-buffer, Win64 ABI/struct-layout, UTF conversion, process quoting,
  ConPTY resize, child exit, and cancellation tests.
- [ ] Run shared `term.k` ANSI/Unicode/search/scrollback tests on all three OSes.
- [ ] Add a headless or startup-smoke mode that creates and closes the native UI.
- [ ] Add large-output, rapid-resize, repeated tab/split, and handle-leak stress tests.

### 20. MSIX/Store output

- [ ] Generate or template `AppxManifest.xml` with the reserved identity:
  - Name: `t3m3d.StemTerminalforWindows`
  - Publisher: `CN=97613709-C254-4F66-AB6B-1EE4BA3D003F`
  - Publisher display name: `t3m3d`
  - Product display name: `Stem: Terminal for Windows`
- [ ] Generate required tile/icon assets from the canonical STEM artwork.
- [ ] Produce a versioned x64 `.msix` and symbol artifact.
- [ ] Invoke `MakeAppx.exe` and `SignTool.exe` through structured argv execution
  for local testing; allow unsigned Store upload when Partner Center will sign it.
- [ ] Validate manifest identity, architecture, minimum OS, executable path,
  display name, signature subject, and package contents before declaring success.
- [ ] Later add ARM64 and an architecture bundle without changing app behavior.

## Conversion-ready definition of done

The Krypton Windows backend is ready for the STEM conversion only when all of
the following are true:

- [ ] A native Krypton GUI executable starts PowerShell 7 through ConPTY.
- [ ] Starship renders completely with `TERM=xterm-256color` and truecolor.
- [ ] Interactive completion stays on the prompt line; `vim`/TUIs and bracketed
  paste work; resize updates ConPTY and the grid.
- [ ] The application uses `term.k`, not a third Windows terminal parser.
- [ ] Tabs, nested splits, pane focus/zoom, scrollback, find, selection, copy/paste,
  settings, profiles, themes, transparency, and session restore match the current release.
- [ ] The Krypton Dark/Light UI has no legacy Windows light controls or unthemed scrollbars.
- [ ] The build has no .NET, WPF, C, C++, Bash, Python, `cmd.exe`, `od`, or `dd` dependency.
- [ ] Windows tests pass, and existing macOS/Linux builds and tests remain green.
- [ ] The generated MSIX passes local package validation and installs/launches on
  a clean supported Windows account.

## Recommended implementation order

1. Fix mutable state; add binary buffers, UTF-16, Win64 structs, handles, and cleanup.
2. Add structured process execution and direct GUI-subsystem PE output.
3. Implement ConPTY plus background read/UI dispatch and resize.
4. Compile and run canonical `term.k` against ConPTY with shared smoke tests.
5. Add DirectWrite/Direct2D renderer plus Windows input/clipboard/IME.
6. Add native window chrome, tabs, recursive splits, menus, and settings controls.
7. Move config/profile/session/crash behavior from the isolated C# bridge.
8. Convert build/test/package flows to KryptScript and produce the MSIX.
9. Remove the C#/WPF bridge only after side-by-side parity testing succeeds.

## Explicitly not required to begin the conversion

These are valuable Ghostty-parity follow-ups, but they should not delay removal
of the C#/WPF bridge once current behavior is matched:

- Kitty graphics protocol and GPU image storage
- Kitty keyboard protocol
- OSC 8 hyperlinks and guarded OSC 52 clipboard
- Synchronized output mode
- Shell-integration prompt/command metadata
- Advanced ligatures and configurable OpenType features
- Serial, Telnet, and mainframe protocol frontends

Design the renderer and terminal metadata APIs so these can be added later
without another platform rewrite.
