# Changelog

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
