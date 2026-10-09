using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Draws the pitch ghost and the score race. Visual only: it never changes notes, pitch, score or timing.
//
// The ghost line is drawn in the note container of the player, in the same coordinate system as the
// game's own notes and pitch indicator. X and Y come from the player's note displayer
// (GetXInPercent / GetYStartAndEndInPercentForMidiNote), the same calls PlayerPitchIndicatorControl uses.
// Time comes from the song position (SongAudioPlayer), not from Unity's Time.
public class GhostRenderer
{
    private readonly PlayerControl playerControl;
    private readonly GhostPlayer ghostPlayer; // null when there is no personal best
    private readonly SongMeta songMeta;
    private readonly GhostModSettings modSettings;
    private readonly Settings settings;

    private AbstractSingSceneNoteDisplayer noteDisplayer;
    private VisualElement overlay;
    private VisualElement virtualPlayerLane;
    private VisualElement hud;
    private Label hudMainLabel;
    private Label hudDiffLabel;
    private Label hudWarningLabel;

    private bool overlayAttached;
    private bool hudCreated;
    private int attachAttempts;
    private bool failed;

    private int currentScore;
    private double songPositionInMillis;
    private Color ghostColor = new Color(1f, 1f, 1f, 0.6f);
    private double xOffsetInMillis;

    // Cache to avoid creating new strings every frame.
    private string lastMainText;
    private string lastDiffText;

    public GhostRenderer(
        PlayerControl playerControl,
        GhostPlayer ghostPlayer,
        SongMeta songMeta,
        GhostModSettings modSettings,
        Settings settings)
    {
        this.playerControl = playerControl;
        this.ghostPlayer = ghostPlayer;
        this.songMeta = songMeta;
        this.modSettings = modSettings;
        this.settings = settings;

        // Derive the ghost color from the player's color so it fits the theme, but make it lighter and transparent
        // so that the player's own pitch indicator and notes stay more prominent.
        if (playerControl.MicProfile != null)
        {
            Color playerColor = playerControl.MicProfile.Color;
            Color lighter = Color.Lerp(playerColor, Color.white, 0.6f);
            ghostColor = new Color(lighter.r, lighter.g, lighter.b, 0.6f);
        }
    }

    public void SetCurrentScore(int score)
    {
        currentScore = score;
    }

    // Called every frame by the scene behaviour.
    public void Update(double songPositionInMillis)
    {
        if (failed)
        {
            return;
        }
        this.songPositionInMillis = songPositionInMillis;

        try
        {
            if (!hudCreated)
            {
                CreateHud();
            }
            if (ghostPlayer != null && !overlayAttached && attachAttempts < 600)
            {
                attachAttempts++;
                TryAttachOverlay();
            }

            UpdateHud();
            if (overlay != null)
            {
                bool visible = modSettings.showPitchGhost;
                overlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                {
                    overlay.MarkDirtyRepaint();
                }
            }
        }
        catch (Exception ex)
        {
            failed = true;
            Debug.LogException(ex);
            GhostLog.Error("Ghost renderer stopped because of an error: " + ex.Message);
        }
    }

    public void Dispose()
    {
        if (overlay != null)
        {
            overlay.RemoveFromHierarchy();
            overlay = null;
        }
        if (virtualPlayerLane != null)
        {
            virtualPlayerLane.RemoveFromHierarchy();
            virtualPlayerLane = null;
        }
        if (hud != null)
        {
            hud.RemoveFromHierarchy();
            hud = null;
        }
    }

    private void CreateHud()
    {
        hudCreated = true;
        VisualElement root = UIDocumentUtils.FindUIDocumentOrThrow().rootVisualElement;

        // Small box at the top center of the screen. Ignores pointer input so it never blocks the game's UI.
        hud = new VisualElement();
        hud.name = "ghostHud";
        hud.pickingMode = PickingMode.Ignore;
        hud.style.position = Position.Absolute;
        hud.style.top = 6f;
        hud.style.left = 0f;
        hud.style.right = 0f;
        hud.style.alignItems = Align.Center;

        VisualElement box = new VisualElement();
        box.pickingMode = PickingMode.Ignore;
        box.style.alignItems = Align.Center;
        box.style.paddingLeft = 14f;
        box.style.paddingRight = 14f;
        box.style.paddingTop = 4f;
        box.style.paddingBottom = 4f;
        box.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);

        hudMainLabel = CreateHudLabel(18f, true);
        hudDiffLabel = CreateHudLabel(26f, true);
        hudWarningLabel = CreateHudLabel(13f, false);
        box.Add(hudMainLabel);
        box.Add(hudDiffLabel);
        box.Add(hudWarningLabel);
        hud.Add(box);
        root.Add(hud);

        if (ghostPlayer != null)
        {
            string warning = GetCompatibilityWarning();
            hudWarningLabel.text = warning;
            hudWarningLabel.style.display = warning.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
        else
        {
            hudWarningLabel.style.display = DisplayStyle.None;
        }
    }

    private Label CreateHudLabel(float fontSize, bool bold)
    {
        Label label = new Label();
        label.pickingMode = PickingMode.Ignore;
        label.style.fontSize = fontSize;
        if (bold)
        {
                    }
        return label;
    }

    // Warns when the personal best was recorded with other detection settings, because then scores are less comparable.
    private string GetCompatibilityWarning()
    {
        GhostDetectorMetadata recorded = ghostPlayer.Run.Detector;
        string currentAlgorithm = settings.PitchDetectionAlgorithm.ToString();
        string currentDifficulty = playerControl.PlayerProfile.Difficulty.ToString();

        List<string> parts = new List<string>();
        if (recorded != null && recorded.Algorithm != currentAlgorithm)
        {
            parts.Add("Ghost recorded with " + recorded.Algorithm + ", current detector: " + currentAlgorithm);
        }
        if (recorded != null && !string.IsNullOrEmpty(recorded.Difficulty) && recorded.Difficulty != currentDifficulty)
        {
            parts.Add("Ghost recorded on " + recorded.Difficulty + ", current difficulty: " + currentDifficulty);
        }
        return string.Join("\n", parts.ToArray());
    }

    private void UpdateHud()
    {
        if (hud == null)
        {
            return;
        }
        bool visible = modSettings.showScoreDifference;
        hud.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (!visible)
        {
            return;
        }

        string mainText;
        string diffText;
        if (ghostPlayer == null)
        {
            mainText = "No personal best yet";
            diffText = "Sing a run to create one";
        }
        else
        {
            int pbTotal = ghostPlayer.FinalScore;
            mainText = "PB " + pbTotal.ToString("N0") + "    YOU " + currentScore.ToString("N0");

            // The race: compare with the score the ghost had at the same position in the song.
            int ghostScoreNow = ghostPlayer.GetScoreAtPosition(songPositionInMillis);
            int difference = currentScore - ghostScoreNow;
            if (currentScore > pbTotal)
            {
                diffText = "NEW PERSONAL BEST!";
            }
            else if (difference > 0)
            {
                diffText = "YOU +" + difference.ToString("N0") + "  (AHEAD)";
            }
            else if (difference < 0)
            {
                diffText = "PB +" + (-difference).ToString("N0");
            }
            else
            {
                diffText = "TIED";
            }
        }

        if (mainText != lastMainText)
        {
            lastMainText = mainText;
            hudMainLabel.text = mainText;
        }
        if (diffText != lastDiffText)
        {
            lastDiffText = diffText;
            hudDiffLabel.text = diffText;
        }
    }

    // Adds a visual-only PB lane beside the real player's lane. We deliberately do not
    // create a PlayerControl: doing so would initialize another microphone and score stream.
    private void TryAttachOverlay()
    {
        noteDisplayer = playerControl.PlayerUiControl.NoteDisplayer;
        if (noteDisplayer == null)
        {
            return;
        }
        if (noteDisplayer is NoNoteSingSceneDisplayer)
        {
            overlayAttached = true;
            GhostLog.Info("Pitch display is disabled in game settings; virtual player lane skipped.");
            return;
        }
        if (ghostPlayer == null)
        {
            overlayAttached = true;
            return;
        }

        VisualElement playerRoot = playerControl.PlayerUiControl.RootVisualElement;
        VisualElement playerContainer = playerRoot != null ? playerRoot.parent : null;
        if (playerContainer == null)
        {
            return;
        }

        // The game uses this container for the real player UI. Give both lanes equal space,
        // matching the stacked layout used when two players sing the same voice.
        playerContainer.style.height = new Length(100f, LengthUnit.Percent);
        playerContainer.style.flexDirection = FlexDirection.Column;
        for (int i = 0; i < playerContainer.childCount; i++)
        {
            VisualElement child = playerContainer[i];
            if (child.name == "spacer")
            {
                child.style.display = DisplayStyle.None;
            }
        }
        playerRoot.style.flexGrow = 1f;
        playerRoot.style.flexBasis = 0f;
        playerRoot.style.minHeight = 0f;

        virtualPlayerLane = new VisualElement();
        virtualPlayerLane.name = "ghostVirtualPlayerLane";
        virtualPlayerLane.pickingMode = PickingMode.Ignore;
        virtualPlayerLane.style.flexGrow = 1f;
        virtualPlayerLane.style.flexBasis = 0f;
        virtualPlayerLane.style.minHeight = 0f;
        virtualPlayerLane.style.flexDirection = FlexDirection.Row;
        virtualPlayerLane.style.paddingLeft = 10f;
        virtualPlayerLane.style.paddingRight = 10f;
        virtualPlayerLane.style.paddingTop = 3f;
        virtualPlayerLane.style.paddingBottom = 5f;
        virtualPlayerLane.style.borderTopWidth = 1f;
        virtualPlayerLane.style.borderTopColor = new Color(1f, 1f, 1f, 0.28f);
        virtualPlayerLane.style.backgroundColor = new Color(0f, 0f, 0f, 0.12f);

        // A compact player-info column on the left mirrors the real PlayerUi layout.
        VisualElement infoPanel = new VisualElement();
        infoPanel.name = "ghostVirtualPlayerInfo";
        infoPanel.pickingMode = PickingMode.Ignore;
        infoPanel.style.width = new Length(18f, LengthUnit.Percent);
        infoPanel.style.minWidth = 110f;
        infoPanel.style.flexShrink = 0f;
        infoPanel.style.justifyContent = Justify.Center;
        infoPanel.style.alignItems = Align.Center;
        infoPanel.style.paddingLeft = 4f;
        infoPanel.style.paddingRight = 4f;
        infoPanel.style.borderRightWidth = 1f;
        infoPanel.style.borderRightColor = new Color(1f, 1f, 1f, 0.22f);

        Label nameLabel = new Label("PERSONAL BEST");
        nameLabel.name = "ghostVirtualPlayerName";
        nameLabel.pickingMode = PickingMode.Ignore;
        nameLabel.style.fontSize = 14f;
        nameLabel.style.color = Color.Lerp(ghostColor, Color.white, 0.25f);
        nameLabel.style.whiteSpace = WhiteSpace.Normal;
        Label scoreLabel = new Label(ghostPlayer.FinalScore.ToString("N0"));
        scoreLabel.name = "ghostVirtualPlayerScore";
        scoreLabel.pickingMode = PickingMode.Ignore;
        scoreLabel.style.fontSize = 20f;
        scoreLabel.style.color = Color.white;
        infoPanel.Add(nameLabel);
        infoPanel.Add(scoreLabel);
        virtualPlayerLane.Add(infoPanel);

        VisualElement noteSurface = new VisualElement();
        noteSurface.name = "ghostVirtualPlayerNotes";
        noteSurface.pickingMode = PickingMode.Ignore;
        noteSurface.style.position = Position.Relative;
        noteSurface.style.flexGrow = 1f;
        noteSurface.style.flexBasis = 0f;
        noteSurface.style.minWidth = 0f;
        noteSurface.style.minHeight = 0f;
        noteSurface.style.overflow = Overflow.Hidden;
        virtualPlayerLane.Add(noteSurface);
        playerContainer.Add(virtualPlayerLane);

        overlay = new VisualElement();
        overlay.name = "ghostPitchOverlay";
        overlay.pickingMode = PickingMode.Ignore;
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0f;
        overlay.style.top = 0f;
        overlay.style.right = 0f;
        overlay.style.bottom = 0f;
        overlay.style.overflow = Overflow.Hidden;
        overlay.generateVisualContent += OnGenerateVisualContent;
        noteSurface.Add(overlay);

        // SentenceDisplayer subtracts microphone delay in GetXInPercent because it is
        // intended for the live pitch indicator. Add it back so historical beats align.
        if (noteDisplayer is SentenceDisplayer && playerControl.MicProfile != null)
        {
            xOffsetInMillis = playerControl.MicProfile.DelayInMillis;
        }

        overlayAttached = true;
        GhostLog.Info("Virtual PB player lane attached beside the real player lane.");
    }

    private float GetX(double millis)
    {
        return noteDisplayer.GetXInPercent(millis + xOffsetInMillis);
    }

    private void OnGenerateVisualContent(MeshGenerationContext context)
    {
        if (failed || ghostPlayer == null || noteDisplayer == null || !modSettings.showPitchGhost)
        {
            return;
        }

        try
        {
            DrawGhost(context.painter2D, overlay.contentRect);
        }
        catch (Exception ex)
        {
            failed = true;
            Debug.LogException(ex);
            GhostLog.Error("Ghost drawing stopped because of an error: " + ex.Message);
        }
    }

    private void DrawGhost(Painter2D painter, Rect rect)
    {
        int count = ghostPlayer.BeatCount;
        if (count == 0 || rect.width <= 0 || rect.height <= 0)
        {
            return;
        }

        double millisPerBeat = ghostPlayer.MillisPerBeat;

        // The displayer must map time to x with growing values. When it does not (for example no sentence
        // is displayed yet), skip drawing instead of drawing everything at one position.
        double firstBeatMillis = ghostPlayer.GetBeatMillis(0);
        if (!(GetX(firstBeatMillis + millisPerBeat) > GetX(firstBeatMillis)))
        {
            return;
        }

        // The x position grows with the song position, so the first visible beat can be found by binary search.
        int low = 0;
        int high = count - 1;
        int first = count;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (GetX(ghostPlayer.GetBeatMillis(mid) + millisPerBeat) >= -0.02f)
            {
                first = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        // Draw the chart's target notes in the virtual player's lane, then overlay the
        // pitch recorded in the PB run. This makes it read as a second singer, not just a trace.
        DrawTargetNotes(painter, rect, first, count);

        painter.lineWidth = 4f;
        painter.strokeColor = ghostColor;
        painter.lineJoin = LineJoin.Round;
        painter.lineCap = LineCap.Round;

        bool pathOpen = false;
        int previousBeat = int.MinValue;
        for (int i = first; i < count; i++)
        {
            GhostBeat beat = ghostPlayer.GetBeat(i);
            double beatMillis = ghostPlayer.GetBeatMillis(i);
            float x0 = GetX(beatMillis);
            if (x0 > 1.02f)
            {
                break;
            }

            int midiNote = GetMidiNoteToDraw(beat);
            if (midiNote < 0)
            {
                // No pitch: break the line so there is no line through silent sections.
                if (pathOpen)
                {
                    painter.Stroke();
                    pathOpen = false;
                }
                continue;
            }

            float x1 = GetX(beatMillis + millisPerBeat);
            Vector2 yRange = noteDisplayer.GetYStartAndEndInPercentForMidiNote(midiNote, beat.Beat);
            float y = (yRange.x + yRange.y) * 0.5f * rect.height;
            Vector2 start = new Vector2(x0 * rect.width, y);
            Vector2 end = new Vector2(x1 * rect.width, y);

            if (pathOpen && beat.Beat == previousBeat + 1)
            {
                painter.LineTo(start);
            }
            else
            {
                if (pathOpen)
                {
                    painter.Stroke();
                }
                painter.BeginPath();
                painter.MoveTo(start);
                pathOpen = true;
            }
            painter.LineTo(end);
            previousBeat = beat.Beat;
        }
        if (pathOpen)
        {
            painter.Stroke();
        }
    }

    private void DrawTargetNotes(Painter2D painter, Rect rect, int first, int count)
    {
        painter.lineWidth = 7f;
        painter.strokeColor = new Color(0.12f, 0.82f, 0.72f, 0.78f);
        painter.lineJoin = LineJoin.Round;
        painter.lineCap = LineCap.Round;

        bool pathOpen = false;
        int previousBeat = int.MinValue;
        int previousMidi = -1;
        for (int i = first; i < count; i++)
        {
            GhostBeat beat = ghostPlayer.GetBeat(i);
            double beatMillis = ghostPlayer.GetBeatMillis(i);
            float x0 = GetX(beatMillis);
            if (x0 > 1.02f)
            {
                break;
            }
            int midi = beat.TargetMidiNote;
            if (midi < 0)
            {
                if (pathOpen) painter.Stroke();
                pathOpen = false;
                previousBeat = int.MinValue;
                previousMidi = -1;
                continue;
            }

            float x1 = GetX(beatMillis + ghostPlayer.MillisPerBeat);
            Vector2 yRange = noteDisplayer.GetYStartAndEndInPercentForMidiNote(midi, beat.Beat);
            float y = (yRange.x + yRange.y) * 0.5f * rect.height;
            Vector2 start = new Vector2(x0 * rect.width, y);
            Vector2 end = new Vector2(x1 * rect.width, y);

            if (pathOpen && beat.Beat == previousBeat + 1 && midi == previousMidi)
            {
                painter.LineTo(start);
            }
            else
            {
                if (pathOpen) painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(start);
                pathOpen = true;
            }
            painter.LineTo(end);
            previousBeat = beat.Beat;
            previousMidi = midi;
        }
        if (pathOpen) painter.Stroke();
    }

    // The ghost shows what the player sang (raw detected pitch).
    // Exception: when the raw pitch was outside of the singable range but the game still counted the beat as correct,
    // use the note the game credited. Returns -1 when nothing should be drawn.
    private int GetMidiNoteToDraw(GhostBeat beat)
    {
        bool rawIsSingable = beat.RecordedMidiNote >= MidiUtils.SingableNoteMin
                             && beat.RecordedMidiNote <= MidiUtils.SingableNoteMax;
        if (rawIsSingable)
        {
            return beat.RecordedMidiNote;
        }
        if (beat.Correct && beat.RoundedRecordedMidiNote >= 0)
        {
            return beat.RoundedRecordedMidiNote;
        }
        return -1;
    }
}
