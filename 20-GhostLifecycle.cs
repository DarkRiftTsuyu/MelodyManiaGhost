using UnityEngine;

public class GhostLifeCycle : IOnLoadMod, IOnDisableMod
{
    public void OnLoadMod()
    {
        Debug.Log("[Ghost] Mod loaded");
    }

    public void OnDisableMod()
    {
        Debug.Log("[Ghost] Mod disabled");
    }
}

// Small logging helper. DebugEnabled is only a switch for verbose logs, it holds no game state.
public static class GhostLog
{
    public static bool DebugEnabled;

    public static void Info(string message)
    {
        Debug.Log("[Ghost] " + message);
    }

    public static void Warn(string message)
    {
        Debug.LogWarning("[Ghost] " + message);
    }

    public static void Error(string message)
    {
        Debug.LogError("[Ghost] " + message);
    }

    public static void Verbose(string message)
    {
        if (DebugEnabled)
        {
            Debug.Log("[Ghost] " + message);
        }
    }
}
