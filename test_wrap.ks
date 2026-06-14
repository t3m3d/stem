import "./term.k"
just run {
  let nl = fromCharCode(10)
  let cr = fromCharCode(13)

  // width 5, height 3. Fill row 0 exactly -> deferred (pending) wrap.
  let st = gridNew(5, 3)
  st = gridFeed(st, "ABCDE", 5, 3)
  kp("fill5      cursor=" + gridCursor(st, 5, 3) + " (xterm: 0,4 pending)")
  st = gridFeed(st, "F", 5, 3)
  kp("fill5+F    cursor=" + gridCursor(st, 5, 3) + " (xterm: 1,1)  render=[" + gridPlain(st, 5, 3) + "]")

  // Fill then CRLF then text (the starship-ish full-width-prompt + newline case).
  let s2 = gridNew(5, 3)
  s2 = gridFeed(s2, "ABCDE" + cr + nl + "XY", 5, 3)
  kp("fill+CRLF  cursor=" + gridCursor(s2, 5, 3) + " (xterm: 1,2)  render=[" + gridPlain(s2, 5, 3) + "]")

  // Fill then bare LF then text (no CR). xterm LF keeps column.
  let s2b = gridNew(5, 3)
  s2b = gridFeed(s2b, "ABCDE" + nl + "XY", 5, 3)
  kp("fill+LF    cursor=" + gridCursor(s2b, 5, 3) + " (xterm: 2,2 — bare LF does NOT clear pending wrap; next char resolves it w/ extra CR+LF)  render=[" + gridPlain(s2b, 5, 3) + "]")

  // Bare LF mid-line: does LF preserve column (xterm) or zero it (CRLF-like)?
  let s3 = gridNew(8, 3)
  s3 = gridFeed(s3, "abc" + nl + "X", 8, 3)
  kp("abc LF X   cursor=" + gridCursor(s3, 8, 3) + " (xterm: 1,4 col preserved; stem zeros -> 1,1)  render=[" + gridPlain(s3, 8, 3) + "]")

  // Multi-wrap: width 4, write 10 chars -> rows 0..2 fill, cursor on row2.
  let s4 = gridNew(4, 4)
  s4 = gridFeed(s4, "0123456789", 4, 4)
  kp("wrap10/4   cursor=" + gridCursor(s4, 4, 4) + " (xterm: 2,2)  render=[" + gridPlain(s4, 4, 4) + "]")
}
