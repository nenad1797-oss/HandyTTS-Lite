# Handy TTS Lite

Tiny companion mod for Nuclear Option: no voice engine, no downloads beyond
kilobytes, unlike Handy TTS. Uses the game's own voice, makes it faster and
cleaner.

Pick **Handy TTS** instead if you want distinct neural voices per player.
Only ever run one of them.

## What it does

- Strips rich-text/color tags and `[N]` player-index prefixes before the game
  speaks, so callouts sound clean.
- Watches the chat backlog and turns the game's own speech rate up while
  speech lags behind, restoring your setting when caught up. Your saved
  setting is never changed.
- Optional: ignore your own echoed lines, ignore server lines.
- Never suppresses game speech. Never touches chat on screen.

## Install

- **NOMM (recommended):** install from the Nuclear Option Mod Manager listing.
- **Manual:** drop the release DLL into `BepInEx/plugins/` next to the game.
  Launch the game.

## Options (F1 config menu)

**General**
- Enabled — master switch. Off = mod fully idle, game speech behaves stock.
- MuteOwn — don't speak your own messages (off by default).
- SpeakServer — speak lines sent as `server` (notices, relays; on by
  default). Turn off if you only want player chat.
- Status — live driver readout (read-only): idle, catch-up rate + backlog,
  or OFF warnings.
- LogLevel — 0 = errors only, 1 = normal, 2 = per-message debug trace.
  Set to 2 only when collecting a bug report, then back to 1.

**Queue**
- CatchUpSec — how many seconds of speech backlog before the rate ramps up
  (2–30, default 3).
- CatchUpMax — how far the rate may climb while catching up (1–3,
  default 2). Very long single messages may push to 3 regardless.

**Fixes**
- StripRichText — remove color/tag markup before speech (on by default).
- StripIndex — remove `[N]` player-index prefixes before speech (on by
  default).

**Storage**
- LogDays — keep the speech audit log this many days (default 7, 0 = off).

## Bug reports

Set F1 → LogLevel = 2, reproduce, then send:

1. `Steam\steamapps\common\Nuclear Option\BepInEx\LogOutput.log`
2. `BepInEx\config\Com.MrNoHands.TtsLite\tts-log.txt`

Set LogLevel back to 1 afterwards. `tts-log.txt` contains chat text you heard
plus player names — only share it if you are comfortable sharing that session.

## Build from source

`dotnet build -c ReleaseLite` (needs the game's DLLs; see HintPaths in the
csproj). Same source also builds Handy TTS with `-c Release`.

## Credits

Our code is MIT (see `LICENSE`). Lite ships no third-party engine or voices.
