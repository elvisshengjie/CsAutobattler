using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// RuntimeInitializeOnLoadMethod(AfterSceneLoad) runs once per play session,
/// but the campaign reloads its map for every round. Systems that create
/// themselves (or attach components to agents) in such a method register here
/// so they run again after every scene load.
/// </summary>
public static class SceneReloadHooks
{
    private static readonly List<Action> hooks = new List<Action>();
    private static bool subscribed;

    public static void RunOnEverySceneLoad(Action hook)
    {
        if (!hooks.Contains(hook))
        {
            hooks.Add(hook);
        }
        if (!subscribed)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            subscribed = true;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        // Static state survives play sessions when domain reload is disabled.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        hooks.Clear();
        subscribed = false;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (Action hook in hooks.ToArray())
        {
            hook();
        }
    }
}
