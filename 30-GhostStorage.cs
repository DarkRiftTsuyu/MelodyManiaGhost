using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

public class GhostSaveOutcome
{
    public bool HadPreviousPersonalBest;
    public int PreviousScore;
    public int NewScore;
    // Strictly greater score than the previous personal best (or there was none).
    public bool IsNewPersonalBest;
}

// Reads and writes ghost files in the mod's persistent data folder:
//   Ghosts/<song-id>/personal_best.json + personal_best.wav
//   Runs/<song-id>/latest.json + latest.wav   (temporary, deleted after the comparison)
// Has no UI code and no event subscriptions.
public class GhostStorage
{
    public const int CurrentFormatVersion = 1;

    private const string PersonalBestJsonName = "personal_best.json";
    private const string PersonalBestWavName = "personal_best.wav";
    private const string LatestJsonName = "latest.json";
    private const string LatestWavName = "latest.wav";

    private readonly string rootFolder;

    public GhostStorage(string modPersistentDataFolder)
    {
        rootFolder = modPersistentDataFolder;
    }

    // Deterministic id of a chart + voice. Artist/title alone are not enough because several charts
    // can share them, so the file name, BPM and a fingerprint of the notes are included.
    public static string CreateSongId(SongMeta songMeta, Voice voice)
    {
        string fileName = songMeta.FileInfo != null ? songMeta.FileInfo.Name : "";
        long bpmTimes1000 = (long)Math.Round(songMeta.BeatsPerMinute * 1000.0);

        long noteFingerprint = 0;
        int noteCount = 0;
        List<Note> notes = SongMetaUtils.GetAllNotes(voice);
        foreach (Note note in notes.OrderBy(it => it.StartBeat).ThenBy(it => it.MidiNote))
        {
            noteCount++;
            noteFingerprint = unchecked(noteFingerprint * 31 + note.StartBeat * 7 + note.Length * 13 + note.MidiNote);
        }

        string text = string.Join("|", new string[]
        {
            (songMeta.Artist ?? "").Trim().ToLowerInvariant(),
            (songMeta.Title ?? "").Trim().ToLowerInvariant(),
            fileName.ToLowerInvariant(),
            bpmTimes1000.ToString(),
            voice.Id.ToString(),
            noteCount.ToString(),
            noteFingerprint.ToString(),
        });
        return Fnv1aHash(text);
    }

    // 64 bit FNV-1a hash as 16 hex characters. Stable across runs and machines.
    private static string Fnv1aHash(string text)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }
    }

    private static string SanitizeFileName(string name)
    {
        return Regex.Replace(name ?? "", "[^A-Za-z0-9_\\-]", "_");
    }

    private string GetGhostDirectory(string songId)
    {
        return Path.Combine(Path.Combine(rootFolder, "Ghosts"), SanitizeFileName(songId));
    }

    private string GetRunDirectory(string songId)
    {
        return Path.Combine(Path.Combine(rootFolder, "Runs"), SanitizeFileName(songId));
    }

    // Returns the personal best of the song, or null when there is none or it cannot be used.
    // Never throws and never deletes files.
    public GhostRun LoadPersonalBest(string songId)
    {
        string path = Path.Combine(GetGhostDirectory(songId), PersonalBestJsonName);
        if (!File.Exists(path))
        {
            return null;
        }

        string problem;
        GhostRun run = TryReadRun(path, songId, out problem);
        if (run == null)
        {
            GhostLog.Error("Ignoring personal best '" + path + "': " + problem);
        }
        return run;
    }

    private GhostRun TryReadRun(string path, string expectedSongId, out string problem)
    {
        problem = null;
        GhostRun run;
        try
        {
            run = JsonConverter.FromJson<GhostRun>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            problem = "cannot parse JSON (" + ex.Message + ")";
            return null;
        }

        if (run == null)
        {
            problem = "file is empty";
        }
        else if (run.FormatVersion != CurrentFormatVersion)
        {
            problem = "unsupported format version " + run.FormatVersion + " (expected " + CurrentFormatVersion + ")";
        }
        else if (run.Song == null || run.Song.SongIdentifier != expectedSongId)
        {
            problem = "belongs to a different song or chart";
        }
        else if (run.Result == null || run.Beats == null || run.Beats.Count == 0)
        {
            problem = "contains no result or no beats";
        }
        if (problem != null)
        {
            return null;
        }

        if (run.ScoreTimeline == null)
        {
            run.ScoreTimeline = new List<GhostScorePoint>();
        }
        return run;
    }

    // Compares a finished run with the stored personal best. Does not write anything.
    public GhostSaveOutcome Evaluate(GhostRun run)
    {
        GhostRun previous = LoadPersonalBest(run.Song.SongIdentifier);
        GhostSaveOutcome outcome = new GhostSaveOutcome();
        outcome.NewScore = run.Result.Score;
        outcome.HadPreviousPersonalBest = previous != null;
        outcome.PreviousScore = previous != null ? previous.Result.Score : 0;
        // Strictly greater: an equal score does not replace the personal best.
        outcome.IsNewPersonalBest = previous == null || run.Result.Score > previous.Result.Score;
        return outcome;
    }

    // Writes the run as temporary "latest" files, then replaces the personal best with them.
    // The json is replaced last because it is the file that marks a personal best as valid.
    // Returns false when something failed (the old personal best stays usable in that case).
    public bool SavePersonalBest(GhostRun run, float[] alignedSamples, int sampleRate)
    {
        string songId = run.Song.SongIdentifier;
        string runDirectory = GetRunDirectory(songId);
        string ghostDirectory = GetGhostDirectory(songId);
        string latestJson = Path.Combine(runDirectory, LatestJsonName);
        string latestWav = Path.Combine(runDirectory, LatestWavName);
        string pbJson = Path.Combine(ghostDirectory, PersonalBestJsonName);
        string pbWav = Path.Combine(ghostDirectory, PersonalBestWavName);

        try
        {
            Directory.CreateDirectory(runDirectory);
            Directory.CreateDirectory(ghostDirectory);

            bool hasRecording = alignedSamples != null && alignedSamples.Length > 0 && sampleRate > 0;
            if (hasRecording)
            {
                WavFileWriter.WriteFile(latestWav, sampleRate, 1, alignedSamples);
                run.RecordingFile = PersonalBestWavName;
            }
            else
            {
                run.RecordingFile = null;
            }
            File.WriteAllText(latestJson, JsonConverter.ToJson(run, true));

            // Keep an unreadable old personal best as backup instead of silently destroying it.
            string problem;
            if (File.Exists(pbJson) && TryReadRun(pbJson, songId, out problem) == null)
            {
                File.Copy(pbJson, Path.Combine(ghostDirectory, "personal_best.unreadable.json"), true);
            }

            if (hasRecording)
            {
                File.Copy(latestWav, pbWav, true);
            }
            File.Copy(latestJson, pbJson, true);

            File.Delete(latestJson);
            if (File.Exists(latestWav))
            {
                File.Delete(latestWav);
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            GhostLog.Error("Failed to save personal best to '" + ghostDirectory + "': " + ex.Message);
            return false;
        }
    }

    public string GetPersonalBestDirectory(string songId)
    {
        return GetGhostDirectory(songId);
    }
}
