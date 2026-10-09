# Melody Mania Ghost
a mario kart style personal best ghost for singing, because beating your own score is the only competition i can reliably lose to

## What is this

a runtime mod for UltraStar Play / Melody Mania that remembers your best run of a song  
next time you sing it, your old self shows up as a second singer in its own lane and you race it live  
no new pitch detector, no python, no onnx, no "approximately what you sang"  
it copies what the game itself heard and scored. that's the whole trick.

## Why I made this

cons of singing alone:

* you have no idea if you're actually improving
* a score number on its own means nothing
* your previous best sits in a save file being smug

pros of the ghost:

* your best run comes back every time
* you see it live, on the same pitch lines as the notes
* a number tells you if you're ahead or behind *right now*, not just at the end

## Features

### Records what the game heard (not a guess)

* hooks `PlayerMicPitchTracker.BeatAnalyzedEventStream`, the same stream the scoring code uses
* saves per beat:
   * raw detected midi note
   * rounded/scored midi note (after the game's joker logic)
   * frequency
   * target note
   * whether the game counted it as correct
* doesn't care if the pitch came from a local mic or the companion app, it's the same event

translation:  
the ghost is a replay of the game's own opinion of you

### The ghost is a second singer

* your PB gets its own lane next to your real one, same stacked layout the game uses when two players sing the same voice
* the lane has the chart's target notes, your old pitch line over them, and a "PERSONAL BEST" label with the score
* it does **not** create a second player, so no extra mic, no extra score stream, nothing in the game's scoring changes
* synced to the song position, not unity time, so it doesn't drift
* line breaks on silence, no giant line across a quiet part
* colour comes from your player colour, lighter and see-through
* if pitch display is turned off in the game settings, the lane is skipped

### Live score race

* `PB 9,472    YOU 8,310` at the top of the screen
* plus a gap against what the ghost had *at this exact point in the song*:
   * `PB +342` when you're behind
   * `YOU +42 (AHEAD)` when you're not
   * `NEW PERSONAL BEST!` once you pass the old total
* score comes from the game's own score control, nothing recalculated

### Personal best per chart

* song id is a hash of artist, title, file name, bpm, voice and a fingerprint of the notes, so two charts with the same name don't share a ghost
* duets get a separate ghost per voice
* new best has to be *strictly* higher. equal score doesn't replace it
* restart or quit mid song = run thrown away, old best untouched
* corrupt or incompatible file = ignored and logged, never deleted (an unreadable one gets backed up before a new best replaces it)

```
<ModPersistentDataFolder>/
  Ghosts/<song-id>/
    personal_best.json
    personal_best.wav
```

### Saved when the song ends

* the run is compared and saved right when the sing scene tears down, using the game's own "song finished" state (with a last-note check as backup)
* the results scene might not give mods a callback in time, so waiting for it could lose your run
* the results screen then just *shows* the outcome: it waits up to 120 frames for the handoff and adds a panel above the restart button
   * `NEW PERSONAL BEST +259` (or `first personal best`)
   * `PERSONAL BEST NOT BEATEN` with PB / you / difference
* plus a notification. the normal results UI is left alone

### Mic recording

* your microphone is saved next to the ghost as a mono wav
* aligned to the song start with the mic delay removed
* samples while paused are skipped
* it's only there so you can listen back. the ghost is the pitch data, not the audio

### Passive. read only.

* never changes pitch, notes, scoring, joker rules, timing or mic input
* one recorder per scene, all subscriptions disposed when the scene ends
* if the mod throws it logs and stops instead of taking the game with it

## How it works

1. you start a song, mod finds the first player with a mic
2. loads your personal best for that chart if there is one
3. every analyzed beat and every score change gets written down
4. the PB lane and score race draw from the stored beats using the song position
5. song ends, the scene tears down
6. game's final score vs stored best
7. higher → saves json + wav
8. results scene opens, panel shows what happened


## Install

1. put the `MelodyManiaGhost` folder into the game's `Mods` folder (same place as `MicRecordingSaver`)
2. enable it in the mod settings
3. sing a song to the end (needs a score above 0)
4. sing it again

**don't rename the files.** the game compiles mod files in name order and later ones use classes from earlier ones

```
modinfo.yml
00-GhostModels.cs
10-GhostModSettings.cs
20-GhostLifecycle.cs
30-GhostStorage.cs
40-GhostRecorder.cs
50-GhostPlayer.cs
60-GhostRenderer.cs
65-GhostSingSceneBehaviour.cs
68-GhostResultPresenter.cs
70-GhostSceneMod.cs
```

## Settings

```
Show ghost of personal best        — on
Record runs and save personal best — on
Show score difference              — on
Show pitch ghost                   — on
Debug logging                      — off (per beat logs, song id, etc)
```

## The json

```json
{
  "FormatVersion": 1,
  "Song":   { "Artist": "...", "Title": "...", "SongIdentifier": "8f2a91d0...", "VoiceId": "P1" },
  "Player": { "Name": "..." },
  "Result": { "Score": 9472, "CorrectBeats": 384, "TotalBeats": 410, "Accuracy": 0.936585, "TimestampUtc": "..." },
  "Detector": { "Algorithm": "Dywa", "SampleRate": 44100, "MicDelayMs": 100, "Difficulty": "Medium" },
  "Beats": [ { "Beat": 32, "RecordedMidiNote": 60, "RoundedRecordedMidiNote": 60, "Frequency": 261.63, "TargetMidiNote": 60, "Correct": true } ],
  "ScoreTimeline": [ { "PositionInMillis": 5000, "Score": 100 } ],
  "RecordingFile": "personal_best.wav"
}
```

## Limits (on purpose)

* one player only (first one with a mic). more than one logs a warning
* no medleys
* no online/shared ghosts, no replaying the ghost's audio
* ghost recorded with a different pitch detector or difficulty = small warning under the score, it still shows
* score 0 runs aren't saved

## Current status

* the PB lane changes the layout of the player container to make room (same as a two player split), so custom themes might look off
* "sentence" note display mode is the most likely place for the ghost to be slightly misaligned
* skipping ahead in a song isn't treated as cheating, your score just ends up lower
* writing the wav happens when the song ends, so there can be a short hitch right there on a new best

if something looks wrong turn on debug logging and look for lines starting with `[Ghost]`