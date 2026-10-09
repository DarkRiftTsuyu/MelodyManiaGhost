using System;
using UniInject;
using UnityEngine;

// Entry point. Called by the game whenever a scene is entered.
//   SingScene           -> create the behaviour that records the run and draws the ghost
//   SingingResultsScene -> compare with the personal best, show the result, save the new personal best
public class GhostSceneMod : ISceneMod
{
    [Inject]
    private GhostModSettings modSettings;

    [Inject]
    private ModObjectContext modObjectContext;

    public void OnSceneEntered(SceneEnteredContext sceneEnteredContext)
    {
        try
        {
            if (sceneEnteredContext.Scene == EScene.SingScene)
            {
                CreateSingSceneBehaviour(sceneEnteredContext);
            }
            else if (sceneEnteredContext.Scene == EScene.SingingResultsScene)
            {
                CreateResultsPresenter(sceneEnteredContext);
            }
        }
        catch (Exception ex)
        {
            // A broken mod must never break the game.
            Debug.LogException(ex);
            GhostLog.Error("Error in scene " + sceneEnteredContext.Scene + ": " + ex.Message);
        }
    }

    private void CreateResultsPresenter(SceneEnteredContext sceneEnteredContext)
    {
        GhostLog.Info("Results scene entered; starting delayed results presenter.");
        GameObject gameObject = new GameObject(nameof(GhostResultPresenterBehaviour));
        GhostResultPresenterBehaviour behaviour = gameObject.AddComponent<GhostResultPresenterBehaviour>();
        sceneEnteredContext.SceneInjector
            .WithBindingForInstance(modSettings)
            .WithBindingForInstance(modObjectContext)
            .Inject(behaviour);
    }

    private void CreateSingSceneBehaviour(SceneEnteredContext sceneEnteredContext)
    {
        GameObject gameObject = new GameObject();
        gameObject.name = nameof(GhostSingSceneBehaviour);
        GhostSingSceneBehaviour behaviour = gameObject.AddComponent<GhostSingSceneBehaviour>();
        sceneEnteredContext.SceneInjector
            .WithBindingForInstance(modSettings)
            .WithBindingForInstance(modObjectContext)
            .Inject(behaviour);
    }
}
