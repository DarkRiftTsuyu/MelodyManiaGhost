using System;
using System.Collections.Generic;
using UnityEngine;

// Records one run of one player. It only observes the game, it never changes anything.
// It has no UI code. The scene behaviour feeds it with the game's events.
public class GhostRecorder
{
    private readonly SongMeta songMeta;
    private readonly PlayerControl playerControl;
    private readonly Settings settings;
    private readonly MicSampleRecorder micSampleRecorder;
    private readonly string songId;
    private readonly double songDurationInSeconds;

    private readonly List<GhostBeat> beats = new List<GhostBeat>();
    private readonly List<GhostScorePoint> scoreTimeline = new List<GhostScorePoint>();

    // Microphone recording (allocated on the first sample event, sized by the song duration like the MicRecordingSaver mod).
    private float[] micSamples;
    private int writtenMicSamples;
    private int micSampleRate;
    private bool isPaused;
    private bool micBufferFullLogged;

    // To align the recording with the song: where in the recording the song position was when playback started.
    private bool songStartCaptured;
    private int writtenMicSamplesAtSongStart;
    private double songPositionAtSongStartInMillis;

    public int BeatCount => beats.Count;
    public string SongId => songId;

    public GhostRecorder(
        SongMeta songMeta,
        PlayerControl playerControl,
        Settings settings,
        string songId,
        double songDurationInSeconds)
    {
        this.songMeta = songMeta;
        this.playerControl = playerControl;
        this.settings = settings;
        this.songId = songId;
        this.songDurationInSeconds = songDurationInSeconds;
        this.micSampleRecorder = playerControl.PlayerMicPitchTracker.MicSampleRecorder;
    }

    // Called for every beat the game analyzed. This is the same event the game uses for scoring.
    public void OnBeatAnalyzed(BeatAnalyzedEvent evt)
    {
        if (evt == null)
        {
            return;
        }

        // Rewinding in the song makes the game analyze beats again. Drop the old data of those beats
        // so that a beat is never recorded twice.
        while (beats.Count > 0 && beats[beats.Count - 1].Beat >= evt.Beat)
        {
            beats.RemoveAt(beats.Count - 1);
        }

        PitchEvent pitchEvent = evt.PitchEvent;
        GhostBeat ghostBeat = new GhostBeat();
        ghostBeat.Beat = evt.Beat;
        // Without a pitch event there is nothing to fabricate: -1 / 0 / not correct.
        ghostBeat.RecordedMidiNote = pitchEvent != null ? evt.RecordedMidiNote : -1;
        ghostBeat.RoundedRecordedMidiNote = pitchEvent != null ? evt.RoundedRecordedMidiNote : -1;
        ghostBeat.Frequency = pitchEvent != null ? pitchEvent.Frequency : 0f;
        ghostBeat.TargetMidiNote = evt.NoteAtBeat != null ? evt.NoteAtBeat.MidiNote : -1;
        // Same rule as PlayerPerformanceAssessmentControl.IsCorrectlySung.
        ghostBeat.Correct = evt.NoteAtBeat != null && evt.NoteAtBeat.MidiNote == evt.RoundedRecordedMidiNote;
        beats.Add(ghostBeat);

        GhostLog.Verbose("Recording beat " + ghostBeat.Beat + " (midi " + ghostBeat.RecordedMidiNote
            + ", rounded " + ghostBeat.RoundedRecordedMidiNote + ", target " + ghostBeat.TargetMidiNote + ")");
    }

    // Called when the game's score of the player changed.
    public void OnScoreChanged(int totalScore, double positionInMillis)
    {
        // Score points after the current position belong to a part that is sung again after a rewind.
        while (scoreTimeline.Count > 0 && scoreTimeline[scoreTimeline.Count - 1].PositionInMillis > positionInMillis)
        {
            scoreTimeline.RemoveAt(scoreTimeline.Count - 1);
        }

        GhostScorePoint point = new GhostScorePoint();
        point.PositionInMillis = positionInMillis;
        point.Score = totalScore;
        scoreTimeline.Add(point);
    }

    public void SetPaused(bool paused)
    {
        isPaused = paused;
    }

    public void OnUpdate(bool isSongPlaying, double songPositionInMillis)
    {
        if (!songStartCaptured && isSongPlaying && songPositionInMillis > 0)
        {
            songStartCaptured = true;
            writtenMicSamplesAtSongStart = writtenMicSamples;
            songPositionAtSongStartInMillis = songPositionInMillis;
        }
    }

    // Called for every chunk of microphone samples. Same technique as the MicRecordingSaver mod.
    public void OnMicRecordingEvent(RecordingEvent evt)
    {
        if (isPaused || evt == null || micSampleRecorder == null)
        {
            return;
        }

        if (micSamples == null)
        {
            micSampleRate = micSampleRecorder.FinalSampleRate.Value;
            if (micSampleRate <= 0)
            {
                return;
            }
            // 1.5 times the song duration as additional buffer, like the MicRecordingSaver mod.
            long maxSampleCount = (long)(songDurationInSeconds * micSampleRate * 1.5);
            if (maxSampleCount <= 0 || maxSampleCount > 200000000)
            {
                GhostLog.Warn("Not recording microphone: unexpected song length (" + songDurationInSeconds + " s)");
                micSampleRate = 0;
                return;
            }
            micSamples = new float[maxSampleCount];
        }

        int count = evt.NewSampleCount;
        if (count <= 0 || evt.NewSamplesStartIndex < 0 || evt.NewSamplesStartIndex + count > evt.MicSamples.Length)
        {
            return;
        }
        if (writtenMicSamples + count > micSamples.Length)
        {
            if (!micBufferFullLogged)
            {
                micBufferFullLogged = true;
                GhostLog.Warn("Microphone buffer is full, the rest of the recording is dropped.");
            }
            return;
        }
        Array.Copy(evt.MicSamples, evt.NewSamplesStartIndex, micSamples, writtenMicSamples, count);
        writtenMicSamples += count;
    }

    // Creates the run data. The score must be the one calculated by the game.
    public GhostRun BuildRun(PlayerProfile playerProfile, int score)
    {
        GhostRun run = new GhostRun();
        run.FormatVersion = GhostStorage.CurrentFormatVersion;

        run.Song.Artist = songMeta.Artist;
        run.Song.Title = songMeta.Title;
        run.Song.SongIdentifier = songId;
        run.Song.VoiceId = playerControl.Voice.Id.ToString();

        run.Player.Name = playerProfile != null ? playerProfile.Name : playerControl.PlayerProfile.Name;

        MicProfile micProfile = playerControl.MicProfile;
        run.Detector.Algorithm = settings.PitchDetectionAlgorithm.ToString();
        run.Detector.SampleRate = micSampleRate;
        if (micProfile != null)
        {
            run.Detector.MicDelayMs = micProfile.DelayInMillis;
            run.Detector.Amplification = micProfile.Amplification;
            run.Detector.NoiseSuppression = micProfile.NoiseSuppression;
            run.Detector.InputFromCompanionClient = micProfile.IsInputFromConnectedClient;
        }
        run.Detector.Difficulty = playerControl.PlayerProfile.Difficulty.ToString();

        int totalBeats = 0;
        int correctBeats = 0;
        foreach (GhostBeat beat in beats)
        {
            if (beat.TargetMidiNote >= 0)
            {
                totalBeats++;
                if (beat.Correct)
                {
                    correctBeats++;
                }
            }
        }
        run.Result.Score = score;
        run.Result.CorrectBeats = correctBeats;
        run.Result.TotalBeats = totalBeats;
        run.Result.Accuracy = totalBeats > 0 ? Math.Round((double)correctBeats / totalBeats, 6) : 0;
        run.Result.TimestampUtc = DateTime.UtcNow.ToString("o");

        run.Beats = new List<GhostBeat>(beats);
        run.ScoreTimeline = new List<GhostScorePoint>(scoreTimeline);
        return run;
    }

    public int CountPitchEvents()
    {
        int count = 0;
        foreach (GhostBeat beat in beats)
        {
            if (beat.RecordedMidiNote >= 0)
            {
                count++;
            }
        }
        return count;
    }

    public int WrittenMicSampleCount => writtenMicSamples;

    // Returns the mono microphone recording aligned with the song (sample 0 = song position 0, microphone delay removed).
    // Returns null when there is no recording.
    public float[] BuildAlignedSamples(out int sampleRate)
    {
        sampleRate = micSampleRate;
        if (micSamples == null || micSampleRate <= 0 || writtenMicSamples <= 0)
        {
            return null;
        }

        MicProfile micProfile = playerControl.MicProfile;
        int micDelayInMillis = micProfile != null ? micProfile.DelayInMillis : 0;
        int overallDelayInMillis = micDelayInMillis + settings.SystemAudioBackendDelayInMillis;
        long delayInSamples = (long)(overallDelayInMillis * 0.001 * micSampleRate);

        // Index in the recording that belongs to song position 0, plus the delay between sound and recording.
        long songStartIndex = 0;
        if (songStartCaptured)
        {
            songStartIndex = (long)Math.Round(writtenMicSamplesAtSongStart - songPositionAtSongStartInMillis * 0.001 * micSampleRate);
        }
        long firstIndex = songStartIndex + delayInSamples;

        long length = writtenMicSamples - firstIndex;
        if (length <= 0 || length > int.MaxValue)
        {
            return null;
        }

        // result[j] = recording[firstIndex + j]; missing samples before the start of the recording are silence.
        float[] result = new float[length];
        long sourceStart = Math.Max(0, firstIndex);
        long targetStart = sourceStart - firstIndex;
        Array.Copy(micSamples, sourceStart, result, targetStart, writtenMicSamples - sourceStart);

        // Make quiet recordings audible (same rule as the MicRecordingSaver mod: only amplify, never reduce).
        float maxAmplitude = 0f;
        for (int i = 0; i < result.Length; i++)
        {
            maxAmplitude = Math.Max(maxAmplitude, Math.Abs(result[i]));
        }
        float targetVolume = 0.75f;
        if (maxAmplitude > 0f && maxAmplitude < targetVolume)
        {
            float scale = targetVolume / maxAmplitude;
            for (int i = 0; i < result.Length; i++)
            {
                result[i] *= scale;
            }
        }
        return result;
    }
}

// A run that reached the end of the song in the sing scene and waits for the results scene.
// Static handoff between the two scenes, same idea as MicRecordingData in the MicRecordingSaver mod.
// It holds finished data only, no event subscriptions.
public class GhostCompletedRun
{
    public GhostRecorder Recorder;
    public SongMeta SongMeta;
    public PlayerProfile PlayerProfile;
    // Last score seen in the sing scene. Used only when the results scene has no score for the player.
    public int LastKnownScore;
    // Captured before teardown saves the PB, so results can show the correct comparison.
    public GhostSaveOutcome SaveOutcome;
    public bool PersonalBestSaved;
}

public static class GhostRunHandoff
{
    private static GhostCompletedRun pending;

    public static bool HasPending => pending != null;

    public static void Set(GhostCompletedRun completedRun)
    {
        pending = completedRun;
    }

    // Returns the pending run and forgets it, so the memory can be released.
    public static GhostCompletedRun Take()
    {
        GhostCompletedRun result = pending;
        pending = null;
        return result;
    }

    public static void Clear()
    {
        pending = null;
    }
}
