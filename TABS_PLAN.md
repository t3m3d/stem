# stem tabs + splits — implementation plan

Status: **foundation landed on branch `tabs`** (session model validated).
Main branch = stable, VT-complete single pane.

## What's done (branch `tabs`)
- `sessNew(cols, rows, shell, term)` — forks a PTY (TERM wrapper), allocates the
  4 plane buffers (gb/ab/bb/ub), blanks them, returns a **session env** holding
  every per-tab field: m, pid, gb, ab, bb, ub, meta, pending, scrollback, title,
  scrollOff, bell, quiet, kick*, selecting/hasSel/sel*.
- `sessPump(s, cols, rows, pal, sbCap, bellMode)` — drains one session's PTY,
  feeds its grid buffers **in place**, answers DSR/DA, runs OSC palette, captures
  scrollback + bell, debounced startup kick. Sets `dirty`. Same path for fg + bg
  tabs. Returns the updated env.
- **Why env, not lists:** native `xs[i]` list indexing SEGFAULTS on linux_x86;
  `envNew/envSet/envGet` round-trip buffer handles correctly (verified). So the
  session table is an env keyed "0".."N-1"; `sessions = envSet(sessions, ""+i, s)`.

## Remaining (the main-loop rewrite)
1. **State:** `sessions` env, `active` (int), `tabCount` (int). `trows = rows - 1`
   (reserve the top row for the tab bar when tabCount>1; trows = rows when ==1).
2. **Init:** replace the inline PTY/buffer init with `sessNew`; store as tab 0.
   Keep GLOBAL: fd/surface/font/pal/config/W/H/cols/rows/shift/ctrl/alt/ptr*/fb*.
3. **Input (wayland event loop):** load active session's m/meta/scrollOff/sel*
   into locals before the loop, run the existing handlers, save them back after.
   Route key bytes to active's m. Add tab keybinds in the KB op3 handler:
   - Ctrl-Shift-T (kc 28) → `sessNew`, append, make active.
   - Ctrl-Shift-Tab / Ctrl-PageUp/Dn → cycle active.
   - Alt-1..9 → jump to tab N.
   - Ctrl-Shift-W → close active (kill its m via fdClose, splice out of sessions,
     decrement tabCount, pick a neighbour; exit if last).
4. **Pump all:** `for i in 0..tabCount-1: si = sessPump(...); sessions=envSet(...);
   if i==active && si.dirty: dirty=1; if waitChild(si.pid)!=0: close tab i.`
   Active title → wlSetTitle.
5. **Resize (TOP configure):** recompute cols/rows; trows; realloc EACH tab's
   buffers to (cols, trows) + ptySetSize + Ctrl-L. Factor a `sessResize(s,...)`.
6. **Render:** kDrawScreen on the ACTIVE session's gb/ab/bb/ub/meta at y offset
   +16 (below the bar), height trows. Then draw the **tab bar** on row 0: one
   segment per tab (number + truncated title), active highlighted (reuse
   fbFillRect + stemDrawChar). Bell flash + scrollback view stay per active tab.

## Splits (phase 2, after tabs)
- A layout tree (binary h/v split nodes; leaves = sessions). Each leaf gets a
  sub-rectangle (x,y,wcols,hrows). gridFeedB/kDrawScreen already take cols/rows,
  but render needs an (x,y) cell offset + per-pane size → add px/py offset args
  to kDrawScreen, or render each pane into its sub-rect. Borders between panes
  (fbFillRect lines). Focus navigation (Ctrl-Shift-arrows). Split keybinds
  (Ctrl-Shift-D vertical, Ctrl-Shift-E horizontal). Resize the focused pane.
- Each leaf is a `sessNew` session sized to its rect; on layout change, resize
  affected leaves.

## Gotchas
- env churn: only envSet a field when it changed (sessPump already does). Avoid
  per-idle-iteration envSet (would leak — no GC).
- OSC 10/11/12 (default fg/bg/cursor) are currently global; per-tab theming would
  need per-session fg/bg fields — deferred (palette via OSC 4 is shared, fine).
- the wrapper file /tmp/.stem-shell-<m> is per session (m differs) — already ok.
