# Handy TTS Lite

Tiny companion mod for Nuclear Option: no voice engine, no downloads beyond
kilobytes. Uses the game's own voice, makes it faster and cleaner.

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

General: Enabled, MuteOwn, SpeakServer, Status (live readout), LogLevel.
Queue: CatchUpSec, CatchUpMax. Fixes: StripRichText, StripIndexPrefix.
Storage: LogDays (audit log).

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
