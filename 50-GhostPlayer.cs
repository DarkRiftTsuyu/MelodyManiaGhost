using System;
using System.Collections.Generic;

// Plays back a stored run: answers "what did the ghost sing / score at this song position?".
// It does not know how the run is stored and does not draw anything.
public class GhostPlayer
{
    public GhostRun Run { get; private set; }

    private readonly SongMeta songMeta;
    private readonly GhostBeat[] beats;
    // Song position in millis for each beat. Calculated once with the game's own beat/time conversion.
    private readonly double[] beatMillis;
    private readonly GhostScorePoint[] scoreTimeline;

    public int BeatCount => beats.Length;
    public int FinalScore => Run.Result.Score;
    public double MillisPerBeat => SongMetaBpmUtils.MillisPerBeat(songMeta);

    public GhostPlayer(GhostRun run, SongMeta songMeta)
    {
        this.Run = run;
        this.songMeta = songMeta;

        beats = run.Beats.ToArray();
        Array.Sort(beats, (a, b) => a.Beat.CompareTo(b.Beat));
        beatMillis = new double[beats.Length];
        for (int i = 0; i < beats.Length; i++)
        {
            beatMillis[i] = SongMetaBpmUtils.BeatsToMillis(songMeta, beats[i].Beat);
        }

        scoreTimeline = run.ScoreTimeline.ToArray();
        Array.Sort(scoreTimeline, (a, b) => a.PositionInMillis.CompareTo(b.PositionInMillis));
    }

    public GhostBeat GetBeat(int index)
    {
        return beats[index];
    }

    public double GetBeatMillis(int index)
    {
        return beatMillis[index];
    }

    // Score of the ghost at the given song position.
    // Uses the score the game had at that position in the stored run.
    // Without a timeline the final score is used.
    public int GetScoreAtPosition(double songPositionInMillis)
    {
        if (scoreTimeline.Length == 0)
        {
            return FinalScore;
        }

        // Last score point at or before the position (binary search).
        int low = 0;
        int high = scoreTimeline.Length - 1;
        int found = -1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (scoreTimeline[mid].PositionInMillis <= songPositionInMillis)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return found >= 0 ? scoreTimeline[found].Score : 0;
    }

    public bool HasScoreTimeline => scoreTimeline.Length > 0;
}
