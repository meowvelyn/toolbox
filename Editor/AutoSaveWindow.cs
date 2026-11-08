using UnityEditor;
using UnityEngine;

/// <summary>
/// EditorWindow for configuring the Auto‑Save system.
/// All changes are immediately persisted via EditorPrefs.
/// </summary>
public class AutoSaveWindow : EditorWindow
{
    [MenuItem("Toolbox/Auto Save Settings", false, 6)]
    private static void OpenWindow()
    {
        var window = GetWindow<AutoSaveWindow>("Auto Save");
        window.minSize = new Vector2(320, 200);
        window.Show();
    }

    // ── Local copies (updated from EditorPrefs on enable) ───
    private bool  enabled;
    private float interval;
    private bool  notify;

    // ─────────────────────────────────────────────────────────
    private void OnEnable()
    {
        enabled  = AutoSaveManager.IsEnabled;
        interval = AutoSaveManager.IntervalMinutes;
        notify   = AutoSaveManager.ShowNotification;
    }

    // ─────────────────────────────────────────────────────────
    private void OnGUI()
    {
        EditorGUILayout.Space(10);

        // ── Header ──────────────────────────────────────
        GUILayout.Label("Auto Save Configuration", EditorStyles.boldLabel);
        EditorGUILayout.Space(6);

        // ── Enabled ─────────────────────────────────────
        EditorGUI.BeginChangeCheck();
        enabled = EditorGUILayout.ToggleLeft(" Auto‑Save Active", enabled);
        if (EditorGUI.EndChangeCheck())
            AutoSaveManager.IsEnabled = enabled;

        EditorGUILayout.Space(4);

        // ── Interval ────────────────────────────────────
        EditorGUI.BeginChangeCheck();
        interval = EditorGUILayout.Slider("Interval (minutes)", interval, 0.5f, 60f);
        if (EditorGUI.EndChangeCheck())
            AutoSaveManager.IntervalMinutes = interval;

        EditorGUILayout.Space(4);

        // ── Notification ────────────────────────────────
        EditorGUI.BeginChangeCheck();
        notify = EditorGUILayout.ToggleLeft(" Show console messages", notify);
        if (EditorGUI.EndChangeCheck())
            AutoSaveManager.ShowNotification = notify;

        EditorGUILayout.Space(12);

        // ── Action buttons ──────────────────────────────
        using (new EditorGUI.DisabledGroupScope(!enabled))
        {
            if (GUILayout.Button("Save Now", GUILayout.Height(28)))
            {
                AutoSaveManager.ForceSave();
            }

            if (GUILayout.Button("Reset Timer", GUILayout.Height(24)))
            {
                AutoSaveManager.ResetTimer();
            }
        }

        EditorGUILayout.Space(10);

        // ── Status area ─────────────────────────────────
        EditorGUILayout.LabelField("Status", enabled ? "Active" : "Paused");
        if (enabled)
        {
            double next = AutoSaveManager.GetMinutesUntilNextSave();
            EditorGUILayout.LabelField("Next save in", $"{next:F1} min");
        }
    }
}
