using System.Collections.Generic;

// Data model of a saved ghost run (serialized to personal_best.json).
// Plain classes with public get/set properties so the game's JSON serializer can read and write them.
// Increase FormatVersion (see GhostStorage.CurrentFormatVersion) when the layout changes.

public class GhostSongMetadata
{
    public string Artist { get; set; }
    public string Title { get; set; }
    // Hash that identifies this exact chart (see GhostStorage.CreateSongId).
    public string SongIdentifier { get; set; }
    // Voice that was sung (P1 / P2 in duets).
    public string VoiceId { get; set; }
}

public class GhostPlayerMetadata
{
    public string Name { get; set; }
}

public class GhostDetectorMetadata
{
    public string Algorithm { get; set; }
    // 0 when the pitch came from a companion app (no local microphone recording).
    public int SampleRate { get; set; }
    public int MicDelayMs { get; set; }
    public int Amplification { get; set; }
    public int NoiseSuppression { get; set; }
    // Difficulty changes the rounding distance of the game, so it matters for comparisons.
    public string Difficulty { get; set; }
    public bool InputFromCompanionClient { get; set; }
}

public class GhostResult
{
    // Score as calculated by the game (PlayerScoreControl / singing results).
    public int Score { get; set; }
    // Beats that had a target note and were scored as correct by the game's rule.
    public int CorrectBeats { get; set; }
    // Analyzed beats that had a target note.
    public int TotalBeats { get; set; }
    public double Accuracy { get; set; }
    public string TimestampUtc { get; set; }
}

// One analyzed beat, copied from the game's BeatAnalyzedEvent.
public class GhostBeat
{
    public int Beat { get; set; }
    // Raw detected MIDI note (-1 when nothing was detected).
    public int RecordedMidiNote { get; set; }
    // MIDI note after the game's rounding and joker rule (-1 when nothing was detected).
    public int RoundedRecordedMidiNote { get; set; }
    public float Frequency { get; set; }
    // MIDI note of the target note at this beat (-1 when there was no note).
    public int TargetMidiNote { get; set; }
    public bool Correct { get; set; }
}

// Score of the game at a point in the song. Used for the live "race" against the ghost.
public class GhostScorePoint
{
    public double PositionInMillis { get; set; }
    public int Score { get; set; }
}

public class GhostRun
{
    public int FormatVersion { get; set; }
    public GhostSongMetadata Song { get; set; } = new GhostSongMetadata();
    public GhostPlayerMetadata Player { get; set; } = new GhostPlayerMetadata();
    public GhostResult Result { get; set; } = new GhostResult();
    public GhostDetectorMetadata Detector { get; set; } = new GhostDetectorMetadata();
    public List<GhostBeat> Beats { get; set; } = new List<GhostBeat>();
    public List<GhostScorePoint> ScoreTimeline { get; set; } = new List<GhostScorePoint>();
    public string RecordingFile { get; set; }
}
