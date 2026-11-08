using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Automatically saves the active scene at a configurable interval.
/// Uses EditorPrefs so settings persist across all Unity projects.
/// Works without any EditorWindow open via [InitializeOnLoad].
/// Compatible with Unity 2022.4 LTS.
/// </summary>
[InitializeOnLoad]
public static class AutoSaveManager
{
    // ─────────────────────────────────────────────────────────
    //  EditorPrefs keys
    // ─────────────────────────────────────────────────────────
    private const string PREF_ENABLED           = "AutoSave_Enabled";
    private const string PREF_INTERVAL_MINUTES  = "AutoSave_IntervalMinutes";
    private const string PREF_SHOW_NOTIFICATION = "AutoSave_ShowNotification";

    // ─────────────────────────────────────────────────────────
    //  Public properties (read/write EditorPrefs internally)
    // ─────────────────────────────────────────────────────────
    /// <summary> Master toggle for the auto‑save system. </summary>
    public static bool IsEnabled
    {
        get => EditorPrefs.GetBool(PREF_ENABLED, true);
        set => EditorPrefs.SetBool(PREF_ENABLED, value);
    }

    /// <summary> Interval in minutes (clamped ≥ 0.5). </summary>
    public static float IntervalMinutes
    {
        get => EditorPrefs.GetFloat(PREF_INTERVAL_MINUTES, 5f);
        set => EditorPrefs.SetFloat(PREF_INTERVAL_MINUTES, Mathf.Max(0.5f, value));
    }

    /// <summary> Show a console log after each auto‑save. </summary>
    public static bool ShowNotification
    {
        get => EditorPrefs.GetBool(PREF_SHOW_NOTIFICATION, true);
        set => EditorPrefs.SetBool(PREF_SHOW_NOTIFICATION, value);
    }

    // ─────────────────────────────────────────────────────────
    //  Runtime state (not serialised – resets per editor session)
    // ─────────────────────────────────────────────────────────
    private static double lastSaveTime;
    private static bool   initialized;

    // ─────────────────────────────────────────────────────────
    //  Initialisation (Unity calls the static ctor on load)
    // ─────────────────────────────────────────────────────────
    static AutoSaveManager()
    {
        Init();
    }

    private static void Init()
    {
        if (initialized) return;

        lastSaveTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += OnEditorUpdate;

        // Optional: subscribe to play‑mode state changes if you want
        // to reset the timer after play mode ends.
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        initialized = true;

        if (ShowNotification)
            Debug.Log("[AutoSave] Initialised. Next save in " + IntervalMinutes + " min.");
    }

    // ─────────────────────────────────────────────────────────
    //  Core update loop
    // ─────────────────────────────────────────────────────────
    private static void OnEditorUpdate()
    {
        if (!IsEnabled) return;
        if (!CanSave) return;

        string scenePath = EditorSceneManager.GetActiveScene().path;
        if (string.IsNullOrEmpty(scenePath)) return;   // unsaved, new scene

        double elapsed = EditorApplication.timeSinceStartup - lastSaveTime;
        if (elapsed < IntervalMinutes * 60.0) return;

        // Timer has elapsed – save if dirty, then always reset the timer
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.isDirty)
        {
            bool saved = SaveScene(scene);
            if (saved && ShowNotification)
            {
                string time = DateTime.Now.ToString("HH:mm:ss");
                Debug.Log($"[AutoSave] Saved scene: {scene.name} at {time}");
            }
        }

        lastSaveTime = EditorApplication.timeSinceStartup;
    }

    // ─────────────────────────────────────────────────────────
    //  Play‑mode handling (optional – resets timer on exit)
    // ─────────────────────────────────────────────────────────
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // Reset timer when leaving play mode so the first save
        // doesn't happen immediately.
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            lastSaveTime = EditorApplication.timeSinceStartup;
        }
    }

    // ─────────────────────────────────────────────────────────
    //  Public API (used by the EditorWindow)
    // ─────────────────────────────────────────────────────────
    /// <summary> Manually trigger a save right now. </summary>
    public static void ForceSave()
    {
        if (!CanSave)
            return;

        var scene = EditorSceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
        {
            Debug.LogWarning("[AutoSave] Can't save an untitled scene.");
            return;
        }

        if (!scene.isDirty)
        {
            if (ShowNotification)
                Debug.Log("[AutoSave] Scene is clean – nothing to save.");
            return;
        }

        bool saved = SaveScene(scene);
        lastSaveTime = EditorApplication.timeSinceStartup;

        if (saved && ShowNotification)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            Debug.Log($"[AutoSave] Manual save: {scene.name} at {time}");
        }
    }

    private static bool CanSave => !EditorApplication.isPlayingOrWillChangePlaymode &&
        !EditorApplication.isCompiling && PrefabStageUtility.GetCurrentPrefabStage() == null;

    private static bool SaveScene(UnityEngine.SceneManagement.Scene scene)
    {
        if (EditorSceneManager.SaveScene(scene))
            return true;

        Debug.LogWarning($"[AutoSave] Could not save scene: {scene.path}");
        return false;
    }

    /// <summary> Reset the internal timer (so the next save waits a full interval). </summary>
    public static void ResetTimer()
    {
        lastSaveTime = EditorApplication.timeSinceStartup;
    }

    /// <summary> Minutes until the next auto‑save attempt. Returns 0 if disabled. </summary>
    public static double GetMinutesUntilNextSave()
    {
        if (!IsEnabled) return 0.0;
        double elapsed = EditorApplication.timeSinceStartup - lastSaveTime;
        double remaining = IntervalMinutes * 60.0 - elapsed;
        return Math.Max(0.0, remaining / 60.0);
    }
}
