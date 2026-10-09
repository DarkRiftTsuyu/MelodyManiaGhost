using System;
using UniInject;
using UniRx;
using UnityEngine;

// Lives in the sing scene. Owns exactly one recorder / player / renderer for this scene instance.
// Starting or restarting a song loads the scene again, which destroys this object (and with it all
// subscriptions, via AddTo(gameObject)) and creates a fresh one. Nothing is shared between runs,
// so events can never be recorded twice.
public class GhostSingSceneBehaviour : MonoBehaviour, INeedInjection
{
    [Inject]
    private GhostModSettings modSettings;

    [Inject]
    private ModObjectContext modObjectContext;

    [Inject]
    private SingSceneControl singSceneControl;

    [Inject]
    private SingSceneData sceneData;

    [Inject]
    private SongAudioPlayer songAudioPlayer;

    [Inject]
    private SongMeta songMeta;

    [Inject]
    private SingSceneFinisher singSceneFinisher;

    [Inject]
    private Settings settings;

    private bool initialized;
    private bool failed;

    private PlayerControl trackedPlayer;
    private GhostStorage storage;
    private GhostRecorder recorder;
    private GhostPlayer ghostPlayer;
    private GhostRenderer renderer;

    // Last values seen while the scene was running (the scene objects may already be gone in OnDestroy).
    private double lastKnownPositionInMillis;
    private int lastKnownBeat;
    private int lastKnownScore;
    private bool songFinished;

    private void Update()
    {
        if (failed)
        {
            return;
        }

        try
        {
            if (!initialized)
            {
                // Same trigger as the MicRecordingSaver mod: wait until the song is loaded.
                if (songAudioPlayer.IsFullyLoaded)
                {
                    initialized = true;
                    Initialize();
                }
                return;
            }

            lastKnownPositionInMillis = songAudioPlayer.PositionInMillis;
            lastKnownBeat = (int)songAudioPlayer.GetCurrentBeat(false);

            // Use the game's own end-of-song state instead of relying only on the final
            // Update() frame. SingSceneFinisher is what actually drives the transition to results.
            if (singSceneFinisher != null && singSceneFinisher.IsSongFinished)
            {
                if (!songFinished)
                {
                    songFinished = true;
                    GhostLog.Info("Game reports song finished at beat " + lastKnownBeat + ".");
                }
            }
            if (trackedPlayer != null && trackedPlayer.PlayerScoreControl != null)
            {
                lastKnownScore = trackedPlayer.PlayerScoreControl.TotalScore;
            }

            if (recorder != null)
            {
                recorder.OnUpdate(songAudioPlayer.IsPlaying, lastKnownPositionInMillis);
            }
            if (renderer != null)
            {
                renderer.Update(lastKnownPositionInMillis);
            }
        }
        catch (Exception ex)
        {
            failed = true;
            Debug.LogException(ex);
            GhostLog.Error("Ghost stopped because of an error: " + ex.Message);
        }
    }

    private void Initialize()
    {
        GhostLog.DebugEnabled = modSettings.debugLogging;
        GhostLog.Info("Sing scene behaviour initialized.");
        GhostRunHandoff.Clear();

        if (sceneData.IsMedley)
        {
            GhostLog.Info("Medley: ghost is not available.");
            return;
        }
        if (!modSettings.enableGhost && !modSettings.enableRecording)
        {
            return;
        }

        trackedPlayer = SelectPlayer();
        if (trackedPlayer == null)
        {
            GhostLog.Info("No player with a microphone found. Ghost is not active.");
            return;
        }

        GhostLog.Info("Tracking player: " + trackedPlayer.PlayerProfile.Name);
        storage = new GhostStorage(modObjectContext.ModPersistentDataFolder);
        string songId = GhostStorage.CreateSongId(songMeta, trackedPlayer.Voice);
        GhostLog.Verbose("Song id: " + songId);

        // Load the personal best (null when there is none, or it is corrupt / incompatible: already logged).
        if (modSettings.enableGhost)
        {
            GhostRun personalBest = storage.LoadPersonalBest(songId);
            if (personalBest != null)
            {
                ghostPlayer = new GhostPlayer(personalBest, songMeta);
                GhostLog.Info("Loaded PB: " + personalBest.Result.Score);
                GhostLog.Verbose("Ghost beats: " + ghostPlayer.BeatCount);
            }
        }

        if (modSettings.enableGhost)
        {
            renderer = new GhostRenderer(trackedPlayer, ghostPlayer, songMeta, modSettings, settings);
        }

        if (modSettings.enableRecording)
        {
            recorder = new GhostRecorder(songMeta, trackedPlayer, settings, songId, songAudioPlayer.DurationInSeconds);
            GhostLog.Info("Started run: " + songMeta.Artist + " - " + songMeta.Title + " (player " + trackedPlayer.PlayerProfile.Name + ")");
        }

        // The game's own analyzed beats, the same stream the scoring uses.
        trackedPlayer.PlayerMicPitchTracker.BeatAnalyzedEventStream
            .Subscribe(evt => OnBeatAnalyzed(evt))
            .AddTo(gameObject);

        // The game's own score.
        trackedPlayer.PlayerScoreControl.ScoreChangedEventStream
            .Subscribe(evt => OnScoreChanged(evt.TotalScore))
            .AddTo(gameObject);

        // Microphone samples, only for saving the recording next to the ghost.
        MicSampleRecorder micSampleRecorder = trackedPlayer.PlayerMicPitchTracker.MicSampleRecorder;
        if (recorder != null && micSampleRecorder != null)
        {
            micSampleRecorder.RecordingEventStream
                .Subscribe(evt => recorder.OnMicRecordingEvent(evt))
                .AddTo(gameObject);
        }

        // Do not record microphone samples while paused.
        singSceneControl.PausedEventStream
            .Subscribe(_ => OnPausedChanged(true))
            .AddTo(gameObject);
        singSceneControl.UnpausedEventStream
            .Subscribe(_ => OnPausedChanged(false))
            .AddTo(gameObject);
    }

    // First local player with a microphone. Only one player is supported for now.
    private PlayerControl SelectPlayer()
    {
        PlayerControl selected = null;
        int candidateCount = 0;
        foreach (PlayerControl playerControl in singSceneControl.PlayerControls)
        {
            if (playerControl.MicProfile == null
                || playerControl.PlayerMicPitchTracker == null
                || !playerControl.PlayerMicPitchTracker.RecordNotes)
            {
                continue;
            }
            candidateCount++;
            if (selected == null)
            {
                selected = playerControl;
            }
        }

        if (candidateCount > 1)
        {
            GhostLog.Warn("Multiple players with a microphone found. Only the first one (" + selected.PlayerProfile.Name + ") gets a ghost.");
        }
        return selected;
    }

    private void OnBeatAnalyzed(BeatAnalyzedEvent evt)
    {
        if (recorder != null)
        {
            recorder.OnBeatAnalyzed(evt);
        }
    }

    private void OnScoreChanged(int totalScore)
    {
        lastKnownScore = totalScore;
        if (renderer != null)
        {
            renderer.SetCurrentScore(totalScore);
        }
        if (recorder != null)
        {
            recorder.OnScoreChanged(totalScore, songAudioPlayer.PositionInMillis);
        }
    }

    private void OnPausedChanged(bool paused)
    {
        if (recorder != null)
        {
            recorder.SetPaused(paused);
        }
    }

    private void OnDestroy()
    {
        try
        {
            GhostLog.Info("Sing scene behaviour is being destroyed. finished=" + songFinished
                + ", initialized=" + initialized
                + ", beatCount=" + (recorder != null ? recorder.BeatCount.ToString() : "null"));

            if (renderer != null)
            {
                renderer.Dispose();
                renderer = null;
            }

            if (recorder == null || trackedPlayer == null)
            {
                GhostLog.Warn("No recorder/player available during scene teardown.");
                return;
            }

            // SingSceneFinisher is the game's authoritative signal that the song reached its natural end.
            // Keep the old last-note check only as a fallback for builds where the finisher injection is absent.
            Note lastNote = trackedPlayer.GetLastNoteInSong();
            bool reachedLastNote = lastNote != null && lastKnownBeat >= lastNote.EndBeat;
            bool reachedEnd = songFinished || reachedLastNote;

            GhostLog.Info("Teardown completion check: gameFinished=" + songFinished
                + ", reachedLastNote=" + reachedLastNote
                + ", lastBeat=" + lastKnownBeat
                + ", beatCount=" + recorder.BeatCount);

            if (reachedEnd && recorder.BeatCount > 0)
            {
                // Save immediately during teardown. The results scene is not guaranteed to
                // dispatch ISceneMod callbacks before it is replaced, so waiting for the
                // results scene can lose the run entirely. We already have the game's own
                // analyzed pitch stream, score, and microphone samples here.
                GhostRun run = recorder.BuildRun(trackedPlayer.PlayerProfile, lastKnownScore);
                GhostLog.Info("Run finished: building PB directly during teardown. score=" + run.Result.Score
                    + ", beats=" + run.Beats.Count
                    + ", pitchEvents=" + recorder.CountPitchEvents()
                    + ", recordingSamples=" + recorder.WrittenMicSampleCount);

                GhostSaveOutcome outcome = null;
                bool personalBestSaved = false;
                if (run.Result.Score > 0 && run.Beats.Count > 0 && storage != null)
                {
                    outcome = storage.Evaluate(run);
                    GhostLog.Info("PB comparison: current=" + outcome.NewScore + " previous="
                        + (outcome.HadPreviousPersonalBest ? outcome.PreviousScore.ToString() : "none")
                        + " newPB=" + outcome.IsNewPersonalBest);

                    if (outcome.IsNewPersonalBest)
                    {
                        int sampleRate;
                        float[] samples = recorder.BuildAlignedSamples(out sampleRate);
                        bool saved = storage.SavePersonalBest(run, samples, sampleRate);
                        if (saved)
                        {
                            personalBestSaved = true;
                            GhostLog.Info("New PB saved to " + storage.GetPersonalBestDirectory(run.Song.SongIdentifier));
                        }
                        else
                        {
                            GhostLog.Error("SavePersonalBest returned false.");
                        }
                    }
                    else
                    {
                        GhostLog.Info("Run was not a new personal best; existing PB kept.");
                    }
                }
                else
                {
                    GhostLog.Warn("Run could not be saved: score=" + run.Result.Score + ", beats=" + run.Beats.Count);
                }

                // Keep the handoff too, so the results presenter can still display the
                // outcome if this build dispatches the results-scene callback.
                GhostCompletedRun completedRun = new GhostCompletedRun();
                completedRun.Recorder = recorder;
                completedRun.SongMeta = songMeta;
                completedRun.PlayerProfile = trackedPlayer.PlayerProfile;
                completedRun.LastKnownScore = lastKnownScore;
                completedRun.SaveOutcome = outcome;
                completedRun.PersonalBestSaved = personalBestSaved;
                GhostRunHandoff.Set(completedRun);
                GhostLog.Info("Run finished, handoff prepared (beats: " + recorder.BeatCount + ", score: " + lastKnownScore + ")");
            }
            else
            {
                GhostLog.Warn("Run discarded. Song did not reach a recognized end state or no pitch beats were recorded.");
            }
            recorder = null;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            GhostLog.Error("Exception during GhostSingSceneBehaviour.OnDestroy: " + ex.Message);
        }
    }
}
