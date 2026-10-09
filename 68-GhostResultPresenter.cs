using System;
using System.Linq;
using UniInject;
using UnityEngine;
using UnityEngine.UIElements;

// Results-scene worker. Scene injection can happen before the sing-scene teardown
// has populated GhostRunHandoff, so wait briefly rather than consuming it too early.
public class GhostResultPresenterBehaviour : MonoBehaviour, INeedInjection
{
    [Inject] private GhostModSettings modSettings;
    [Inject] private ModObjectContext modObjectContext;
    private int framesWaited;
    private bool completed;

    private void Update()
    {
        if (completed) return;
        framesWaited++;
        try
        {
            if (!GhostRunHandoff.HasPending)
            {
                if (framesWaited == 1 || framesWaited == 10 || framesWaited == 30)
                    GhostLog.Info("Results presenter waiting for completed-run handoff (frame " + framesWaited + ").");
                if (framesWaited < 120) return;
                completed = true;
                GhostLog.Warn("No completed-run handoff appeared within 120 frames; results panel skipped.");
                return;
            }

            GhostCompletedRun pending = GhostRunHandoff.Take();
            completed = true;
            GhostResultPresenter.Present(pending, modSettings, modObjectContext);
        }
        catch (Exception ex)
        {
            completed = true;
            Debug.LogException(ex);
            GhostLog.Error("Results presenter failed: " + ex.Message);
        }
    }
}

public static class GhostResultPresenter
{
    public static void Present(GhostCompletedRun pending, GhostModSettings modSettings, ModObjectContext modObjectContext)
    {
        GhostLog.Info("Results presenter received run handoff.");
        if (pending == null || pending.Recorder == null || !modSettings.enableRecording)
        {
            GhostLog.Info("No presentable run or recording is disabled.");
            return;
        }

        SingingResultsSceneData resultsData = SceneNavigator.GetSceneData(new SingingResultsSceneData());
        if (resultsData == null || resultsData.IsMedley || resultsData.SongMetas == null
            || !resultsData.SongMetas.Contains(pending.SongMeta))
        {
            GhostLog.Warn("Results are for a different song or a medley; skipping ghost result panel.");
            return;
        }

        // Use the game's authoritative final score when available, but preserve the PB
        // comparison made during teardown because the new PB may already have been saved.
        int score = pending.LastKnownScore;
        if (resultsData.PlayerProfiles != null && resultsData.PlayerProfiles.Contains(pending.PlayerProfile))
        {
            ISingingResultsPlayerScore playerScore = resultsData.GetPlayerScores(pending.PlayerProfile);
            if (playerScore != null)
            {
                score = playerScore.NormalNotesTotalScore + playerScore.GoldenNotesTotalScore
                    + playerScore.PerfectSentenceBonusTotalScore + playerScore.ModTotalScore;
            }
        }

        GhostRun run = pending.Recorder.BuildRun(pending.PlayerProfile, score);
        GhostSaveOutcome outcome = pending.SaveOutcome;
        if (outcome == null)
        {
            // Fallback for runs created by an older or alternate handoff path.
            GhostStorage storage = new GhostStorage(modObjectContext.ModPersistentDataFolder);
            outcome = storage.Evaluate(run);
            GhostLog.Warn("No teardown PB outcome was handed off; used fallback comparison.");
        }
        else
        {
            // Update only the displayed current score; keep previous-score/new-PB decision
            // from before teardown persistence to avoid comparing the new PB with itself.
            outcome.NewScore = score;
        }

        GhostLog.Info("Displaying results outcome: current=" + outcome.NewScore + " previous="
            + (outcome.HadPreviousPersonalBest ? outcome.PreviousScore.ToString() : "none")
            + " newPB=" + outcome.IsNewPersonalBest + " alreadySaved=" + pending.PersonalBestSaved);
        ShowOutcome(outcome);
    }

    private static void ShowOutcome(GhostSaveOutcome outcome)
    {
        string title;
        string detail;
        if (outcome.IsNewPersonalBest)
        {
            title = "NEW PERSONAL BEST";
            detail = outcome.HadPreviousPersonalBest
                ? outcome.NewScore.ToString("N0") + "  (+" + (outcome.NewScore - outcome.PreviousScore).ToString("N0") + ")"
                : outcome.NewScore.ToString("N0") + "  — first personal best";
        }
        else
        {
            title = "PERSONAL BEST NOT BEATEN";
            detail = "PB: " + outcome.PreviousScore.ToString("N0") + "  |  You: " + outcome.NewScore.ToString("N0")
                + "  (" + (outcome.NewScore - outcome.PreviousScore).ToString("+#,##0;-#,##0;0") + ")";
        }

        try
        {
            UIDocument document = UIDocumentUtils.FindUIDocumentOrThrow();
            VisualElement root = document.rootVisualElement;
            VisualElement restartButton = root.Q(R.UxmlNames.restartButton);
            if (restartButton == null || restartButton.parent == null)
                throw new InvalidOperationException("Could not find restart button anchor in results UI.");

            // Avoid duplicate panels if another results callback also invokes this presenter.
            VisualElement existing = root.Q("ghostResultsPanel");
            if (existing != null) existing.RemoveFromHierarchy();

            VisualElement panel = new VisualElement { name = "ghostResultsPanel", pickingMode = PickingMode.Ignore };
            panel.style.alignItems = Align.Center;
            panel.style.paddingLeft = 18;
            panel.style.paddingRight = 18;
            panel.style.paddingTop = 10;
            panel.style.paddingBottom = 10;
            panel.style.marginTop = 8;
            panel.style.marginBottom = 8;
            panel.style.backgroundColor = new Color(0.035f, 0.055f, 0.085f, 0.94f);
            panel.style.borderTopLeftRadius = 8;
            panel.style.borderTopRightRadius = 8;
            panel.style.borderBottomLeftRadius = 8;
            panel.style.borderBottomRightRadius = 8;

            Label heading = new Label(title);
            heading.style.fontSize = 22;
            heading.style.color = outcome.IsNewPersonalBest ? new Color(1f, 0.82f, 0.2f) : new Color(0.7f, 0.85f, 1f);
            Label score = new Label(detail);
            score.style.fontSize = 17;
            score.style.color = Color.white;
            panel.Add(heading);
            panel.Add(score);

            VisualElement parent = restartButton.parent;
            parent.Insert(parent.IndexOf(restartButton), panel);
            GhostLog.Info("Personal-best panel added to results UI.");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            GhostLog.Error("Could not add personal-best panel to results screen: " + ex.Message);
        }

        NotificationManager.CreateNotification(Translation.Of(title + ": " + detail));
    }
}
