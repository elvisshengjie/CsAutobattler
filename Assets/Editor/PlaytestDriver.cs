using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batchmode smoke playtest: enters Play Mode, drives the campaign UI by
/// reflection (starter, buy, deploy, tactic), lets a battle run, captures
/// screenshots and writes every warning/error to a report.
/// ART_OUT=<dir> Unity -batchmode -projectPath . -executeMethod PlaytestDriver.Run
/// </summary>
[InitializeOnLoad]
public static class PlaytestDriver
{
    private const string ActiveKey = "PlaytestDriver.Active";
    private const string StartKey = "PlaytestDriver.Start";
    private const string StepKey = "PlaytestDriver.Step";
    private const float BattleSeconds = 75f;

    private static readonly List<string> Problems = new List<string>();
    private static readonly StringBuilder Report = new StringBuilder();
    private static double stepTime;

    static PlaytestDriver()
    {
        if (!SessionState.GetBool(ActiveKey, false))
        {
            return;
        }
        Application.logMessageReceived += OnLog;
        EditorApplication.update += Tick;
    }

    public static void Run()
    {
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetInt(StepKey, 0);
        EditorSceneManager.OpenScene("Assets/Scenes/Map1.unity");
        EditorSettings.asyncShaderCompilation = false;
        Application.logMessageReceived += OnLog;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    private static string OutDir => System.Environment.GetEnvironmentVariable("ART_OUT") ?? ".";

    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            Problems.Add($"[{type}] {message}\n{stack}");
        }
        else if (message.Contains(" died.") || message.StartsWith("Initial team tactic") ||
                 message.Contains("Round") && message.Contains("winner"))
        {
            Report.AppendLine($"{Time.time:0.0}s {message}");
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }

        int step = SessionState.GetInt(StepKey, 0);
        double now = EditorApplication.timeSinceStartup;
        if (stepTime <= 0)
        {
            stepTime = now;
        }
        double elapsed = now - stepTime;
        CampaignManager campaign = CampaignManager.Instance;

        switch (step)
        {
            case 0 when elapsed > 3 && campaign != null:
                ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, "ui_menu.png"));
                Next(step);
                break;
            case 1 when elapsed > 1:
                Invoke(campaign, "StartNewCampaign");
                Next(step);
                break;
            case 2 when elapsed > 3:
                ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, "ui_draft.png"));
                Next(step);
                break;
            case 3 when elapsed > 1:
                Invoke(campaign, "ChooseStarter", 0);
                Invoke(campaign, "BuyCharacter", 0);
                Report.AppendLine($"Roster after buy: {campaign.Roster.Count}, gold {campaign.Gold}");
                Next(step);
                break;
            case 4 when elapsed > 1:
                ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, "ui_prep.png"));
                Next(step);
                break;
            case 5 when elapsed > 1:
                Invoke(campaign, "CommitDeploymentAndOpenTactics");
                Next(step);
                break;
            case 6 when elapsed > 1 && TeamTacticManager.Instance != null:
                TeamTacticManager.Instance.SelectInitialTactic(InitialTeamTactic.FastExecute);
                Next(step);
                break;
            case 7 when elapsed > 12:
                Capture("play_battle_a");
                ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, "ui_battle_a.png"));
                CountModels();
                Next(step);
                break;
            case 8 when elapsed > 15:
                Capture("play_battle_b");
                ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, "ui_battle_b.png"));
                Next(step);
                break;
            case 9 when elapsed > BattleSeconds || (RoundManager.Instance != null && RoundManager.Instance.HasWinner):
                Report.AppendLine($"Round state: {RoundManager.Instance?.CurrentState} winner={RoundManager.Instance?.HasWinner} {RoundManager.Instance?.Winner} {RoundManager.Instance?.WinnerReason}");
                Capture("play_end");
                Finish();
                break;
        }
    }

    private static void Next(int step)
    {
        SessionState.SetInt(StepKey, step + 1);
        stepTime = EditorApplication.timeSinceStartup;
        Report.AppendLine($"step {step} done at t={Time.time:0.0}");
    }

    private static void Invoke(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(
            method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (info == null)
        {
            Problems.Add("Missing method " + method);
            return;
        }
        info.Invoke(target, args);
    }

    private static void CountModels()
    {
        int models = 0;
        int agents = 0;
        foreach (AgentStats agent in Object.FindObjectsByType<AgentStats>(FindObjectsInactive.Exclude))
        {
            if (agent.GetComponent<AgentController3D>() == null) continue;
            agents++;
            AgentCharacterModel model = agent.GetComponent<AgentCharacterModel>();
            if (model != null && model.IsBuilt) models++;
        }
        Report.AppendLine($"Active agents: {agents}, with character models: {models}");
    }

    private static void Capture(string name)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            Problems.Add("No main camera for capture " + name);
            return;
        }
        const int w = 1600, h = 900;
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 24);
        RenderTexture previous = camera.targetTexture;
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        Texture2D texture = new Texture2D(w, h, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), texture.EncodeToPNG());
        camera.targetTexture = previous;
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        Object.DestroyImmediate(texture);
    }

    private static void Finish()
    {
        StringBuilder output = new StringBuilder();
        output.AppendLine("== Report");
        output.Append(Report);
        output.AppendLine($"== Problems ({Problems.Count})");
        foreach (string problem in Problems)
        {
            output.AppendLine(problem);
        }
        File.WriteAllText(Path.Combine(OutDir, "playtest_report.txt"), output.ToString());
        SessionState.SetBool(ActiveKey, false);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(Problems.Count == 0 ? 0 : 3);
    }
}
