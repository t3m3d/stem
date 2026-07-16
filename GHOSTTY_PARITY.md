# STEM ↔ Ghostty parity roadmap

Baseline reviewed 2026-07-15 against Ghostty's official
[feature overview](https://ghostty.org/docs/features),
[shell-integration guide](https://ghostty.org/docs/features/shell-integration),
[configuration reference](https://ghostty.org/docs/config/reference), and
[project roadmap](https://github.com/ghostty-org/ghostty#roadmap-and-status).

Ghostty parity means matching its standards, speed, modern protocols, and
platform-native quality—not copying its interface. STEM remains Krypton-first
and also treats Windows as a first-class platform.

## Current matrix

| Capability | Ghostty target | STEM macOS | STEM Linux | STEM Windows | Next milestone |
|---|---|---:|---:|---:|---|
| Common VT/ANSI + truecolor | Complete xterm-oriented coverage | Strong | Strong | Baseline | Shared conformance corpus |
| Alternate screen / cursor / bracketed paste | Yes | Yes | Yes | Yes | Expand DEC mode tests |
| Scrollback + find | Yes | Yes | Yes | Yes | Prompt navigation |
| Selection + clipboard | Rich word/line/command selection | Yes | Yes | Viewport word/line selection | Cross-history selection |
| Mouse + focus reporting | Yes | Yes | Yes | Not yet | Windows SGR 1006 + focus events |
| Multiple windows / tabs / splits | Yes | Yes | Single session | Independent tabs | Windows splits, then Linux tabs/splits |
| Persistent configuration / themes | Extensive | Yes | Yes | Expanded .conf + visual hot reload | Shared schema + named themes |
| Native UI | SwiftUI/AppKit + GTK | Cocoa | Wayland | WPF | Preserve native platform surfaces |
| GPU rendering | Metal / OpenGL | CPU/native text | CPU framebuffer | CPU WPF | Renderer command IR + platform GPU backends |
| Grapheme clusters / emoji | Yes | Partial | Partial | Partial | Shared width/grapheme engine |
| Ligatures / font features | Yes | Native-font partial | No | Native-font partial | Shaping abstraction |
| OSC 8 hyperlinks / OSC 52 clipboard | Yes | Partial URL handling | No | No | Shared OSC metadata plane + permission policy |
| Synchronized output | Yes | No | No | No | DEC mode 2026 frame transactions |
| Kitty keyboard protocol | Yes | No | No | No | Parser/input protocol phase |
| Kitty graphics protocol | Yes | No | No | No | Image store + GPU texture path |
| Shell integration | Rich prompt/CWD metadata | No | No | No | PowerShell first, then zsh/bash/fish |
| Embeddable terminal core | `libghostty` | Internal core | Internal core | Temporary duplicate | Stable Krypton terminal-core API |

## Delivery order

### P0 — daily-driver parity

1. Complete Windows scrollback, selection, clipboard, and configuration.
2. Extend the Windows session-tab host with pane splitting and layout persistence.
3. Add Windows mouse/focus reporting and robust resize/reflow behavior.
4. Bring Linux window/tab/split organization up to the macOS frontend.

### P1 — compatibility parity

1. Build a shared VT conformance corpus derived from ECMA-48, xterm behavior,
   and protocol-origin tests.
2. Add grapheme clustering, Unicode width tables, combining marks, emoji ZWJ,
   and RTL-safe cluster storage while retaining an LTR terminal layout.
3. Add OSC 8, guarded OSC 52, synchronized output, styled underlines, palette
   queries, and light/dark notifications.
4. Add shell integration for PowerShell, zsh, bash, fish, and nushell: working
   directory, prompt marks, command boundaries, and close-at-prompt detection.

### P2 — performance parity

1. Define a renderer-neutral Krypton display-list API so `term.k` is the only
   terminal state engine.
2. Add Direct3D/DirectWrite on Windows, Metal on macOS, and OpenGL/Vulkan on
   Linux behind that API. Temporary native-language bridges are acceptable
   until Objective-K can own each backend.
3. Separate PTY read, parser, render preparation, and presentation scheduling;
   add frame coalescing and benchmark fixtures.
4. Profile memory layout, scrollback compression, parser throughput, latency,
   and large-output behavior before applying low-level optimization.

### P3 — modern graphics and native polish

1. Kitty graphics with bounded image storage and GPU texture lifecycle.
2. Ligatures and configurable font features.
3. Settings UI, theme browser, session restoration, quick terminal, automation,
   crash reporting, and platform-native accessibility.
4. Publish the stable Krypton terminal core as an embeddable library.

## Architecture rule

No parity work may regress `stem.ks`, `stem_wl.ks`, or `term.k`. New platform
bridges stay isolated until the Krypton/Objective-K backend can absorb them.
Behavioral fixes should move into the shared Krypton core whenever that backend
can express them safely.

