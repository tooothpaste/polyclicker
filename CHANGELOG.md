# Changelog

## 1.2 — 2026-09-03

### Performance

- Resizing the window takes about half the CPU it did: with a dozen cards,
  7 ms of CPU per size tick before, 3.5 ms now. The cards' text is drawn
  once per string and blitted after that, the box outlines and glyphs come
  from caches, and the form places its few controls itself instead of
  running the layout engine. The picture is the same to the pixel;
  `tests\run-perf.ps1` measures it and `tests\pixdiff.ps1` checks it.
- Macro playback at 1000 pointer samples a second went from about 17% of a
  core to 2-4%: samples closer together than 2 ms are merged, and the
  cursor is only pinned in place ahead of a press.

### Dialogs

- The delete/remove/overwrite questions are app dialogs now, centered on
  the window and themed, with the verb on the button instead of Yes/No.
- Dialogs opened on a scaled display are centered after scaling, not
  before; the macro editor no longer shows the stock window icon.

### Macros

- Step timing randomisation (gear → Randomise → Steps ±): every press and
  release in a take lands up to that many milliseconds from its recorded
  moment, so repeated passes don't fire identically. Pointer samples keep
  their exact path.

### Profiles

- Settings → Export profile… writes the current cards and every take they
  use as one zip, laid out like `%APPDATA%\Polyclicker`; unpacking it there
  on another machine is the import.

### Packaging

- An installer (`installer\make-installer.ps1`, Inno Setup): per-user,
  no admin prompt, Start Menu entry, uninstaller. Settings and takes are
  left alone by install and uninstall. Unsigned for now.
- The regression harness lives in the repo (`csharp\tests`); see the
  README.

### Scaling

- The app is per-monitor DPI-aware now. Before, it drew at 96 dpi and
  let Windows stretch the bitmap, so on a 125% or 150% display everything
  was both oversized and blurry. Every layout number scales with the
  monitor's DPI, text is drawn at native resolution, and the glyphs are
  redrawn rather than resampled. Dragging a window to a monitor with a
  different scale, or changing the scale while the app runs, re-lays it
  out at the new size instead of stretching it.
- Zoom: Ctrl+= and Ctrl+- step the UI size, Ctrl+0 resets it - in any
  window, live, remembered. Settings → Appearance → UI size is the same
  setting as a list. It sits on top of the display scale: on a laptop
  Windows scales to 125%, 80% brings the layout back to its 100% size.
  `POLYCLICKER_UISCALE` in the environment overrides it, for support and
  for the harness.

### Fixes

- No more "save before…?" questions. Switching profiles and closing the
  macro editor just do it; saving is the save button (Ctrl+S in the
  editor). Instead, the title bar carries an asterisk whenever the cards
  differ from their profile, or a take differs from its file. The editor
  decides "unsaved" the way the main window does — by comparing against
  the file, not by counting edits — so clicking into a cell and leaving
  it alone doesn't count, and undoing back to the saved state reads as
  clean.
- The dropdown arrows on cards are drawn strokes now, centered like the
  profile menu's, instead of a font glyph that sat low.
- Settings → Restore defaults put the kill switch on Ctrl+Alt+F10; the
  real default is Ctrl+Alt+K, and the button now reads the defaults from
  the same place a fresh install does.

### Hold down

- A clicker's first beat fires the moment it starts instead of one silent
  interval later, so a hold-duration cycle is press first, release after —
  the release phase sits at the end of every click, including the first.

- A toggle next to the interval field holds the input down instead of
  clicking: the button (or key) goes down once and stays down for the
  whole run, released the moment the card stops — by hotkey, kill switch,
  its stop-after clock, or its window gate closing. The gate re-presses
  when the window comes back. Works for foreground and background
  clicking. While it's on, the interval doesn't apply and its field reads
  "held". A held key repeats its down-event like real typematic — a game
  that clears its input state when a menu opens re-notices the key on the
  next repeat, exactly as it would a physical finger. A macro takes the
  input over while it plays; the hold re-asserts once playback goes
  quiet. Takes that stored a 100% hold duration migrate to the toggle;
  the hold duration itself now tops out at 99.

### Macro editor

- Each macro card has a timeline button that opens its take in an editor:
  one row per gesture — a move run, a click, a drag, a key press, a scroll
  burst — with its start time, the pause before it, and its length. The
  window is resizable and doesn't block the main window; several takes can
  be open at once. A key press is always one row, even when its release
  lands after other input (typing rollover, a modifier held across clicks).
- Rows can be deleted (deleting one half of a press removes the other half
  too, so an edited take can never strand a key or button held down), the
  pause before any row and the length of any step edit in place — click a
  wait or length cell, type, Enter — and every coordinate edits in place
  the same way: one click on a click, a move run, or a drag opens where
  it lands ("x, y", or "x, y → x, y" for a drag; one pair slides the whole
  drag). While a position is being typed, a dot on screen marks the spot,
  the same preview the cards give. A move run bends to a new endpoint,
  anchored where it starts. The take previews once from any row through
  the ordinary engine. A key held across other steps says so on its row.
- Playback keeps a 10 ms floor around every press and release, whatever
  the speed setting: applications sample input by the frame, and a click
  or key press compressed below that — by raised speed, or by editing a
  step down to a few milliseconds — is a gesture the app never sees, or
  sees out of order. Pointer samples still compress freely, so speeding a
  take up speeds up its motion without breaking its clicks.
- The whole editor drives from the keyboard, with the usual grammar:
  Ctrl+C/X/V copy, cut, and paste steps, Ctrl+D duplicates, Ctrl+Up/Down
  (or Alt+Up/Down) moves them, Ins adds, Del deletes, Enter/F2 edits, F5
  previews, Ctrl+Z undoes, Ctrl+S saves. In a cell, Tab confirms and moves on — wait,
  length, next row's wait — Shift+Tab walks back, Enter confirms and
  returns to the list, Esc cancels just the cell. The clipboard format is
  the file format, so steps paste between open editors and read as plain
  macro lines anywhere else. The take's name is an editable field at the
  top, like a card's — committing it renames the file. The toolbar is a
  row of icon chips, each with its own tooltip.
- Add step inserts input that was never recorded — clicks, double clicks,
  key presses, scrolls, moves — so a macro can be built in the editor from
  nothing: the edit button on a card with no macro starts a blank take.
- Rename moved from the card into the editor's name field. Undo, save in
  place, or save as a copy. The file format is unchanged; edited takes
  load in older versions.

### Later starts

- Start delay per card, in seconds: the hotkey arms the card and the first
  click (or macro pass) waits out the countdown. The hotkey again cancels.
- Scheduled start per card (HH:MM, 24-hour): the card starts at the next
  such time after the hotkey. With a delay too, the delay counts from the
  scheduled time. Both live in the advanced dialog's Trigger group.
- While a card waits, its status line shows the countdown. Stop-on-input
  and the stop-after-seconds limit count from when clicking begins, not
  from the hotkey — typing during a countdown doesn't cancel it.

### Update check

- Settings asks the GitHub releases API for the latest version when it
  opens, and again on its button; the result is a line beside the button
  and, for a newer release, the button becomes a link to its page. Nothing
  else is sent and nothing is downloaded. Nothing runs at startup, and
  there is no prompt. Failures (offline, blocked) are silent log lines.

## 1.1 — 2026-08-24

### Macros

- Playback speed per card, 10–2000% of the recorded pace. The whole timeline
  scales — gaps and press lengths alike; the loop gap between repeats does
  not.
- Loop toggle per card: repeat until stopped (the 1.0 behaviour, still the
  default) or play once per press.
- The loop gap moved to the advanced dialog and may be 0 (replay
  back-to-back).
- Escape records into a take like any other key; only the record key stops
  recording.
- Stopping a macro no longer moves the cursor to a position remembered from
  an earlier clicker run.

### Stop on input

- Split into "stop when I use the mouse" and "stop when I use the keyboard",
  set per card. Files from 1.0 seed both from the old combined setting.
- A macro's own playback no longer counts as input: pointer movement is
  ignored while the take is driving the pointer, and replayed clicks and
  keys never stop the card. Real clicks, keys, and movement during the loop
  gap still do.
- Scrolling the wheel counts as using the mouse.

### Hotkeys

- The punctuation row (`` ; = , - . / ` [ \ ] ' ``) works as a hotkey or
  custom key.
- Keys with no friendly name round-trip through the settings file instead of
  silently unbinding on the next load.

### Cards

- A macro card shows its loop toggle and speed in place of the interval; the
  advanced dialog shows only the settings the card's input type uses.

### Data folder

- Startup logs the data folder and any migrations, and warns when the
  process was launched inside an app container that redirects `%APPDATA%` —
  the case where two instances of the same exe silently diverge into
  separate configs.

## 1.0 — 2026-08-19

Initial release.
