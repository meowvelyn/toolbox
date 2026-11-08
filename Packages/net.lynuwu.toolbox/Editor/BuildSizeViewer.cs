/**
 * VRC Build Size Viewer
 * Created by MunifiSense
 * https://github.com/MunifiSense/VRChat-Build-Size-Viewer
 */

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;

public class BuildSizeViewer : EditorWindow {

    public class BuildObject {
        public string size;
        public string percent;
        public string path;
    }

    List<BuildObject> buildObjectList;
    List<string> uncompressedList;
    private const string AssetListHeader = "Used Assets and files from the Resources folder, sorted by uncompressed size:";
    private const string ReportEnd = "-------------------------------------------------------------------------------";
    float win;
    float w1;
    float w2;
    float w3;
    string totalSize;
    bool buildLogFound = false;
    Vector2 scrollPos;

    [MenuItem("Toolbox/VRC Build Size Viewer", false, 9)]

    public static void ShowWindow() {
        EditorWindow.GetWindow(typeof(BuildSizeViewer));
    }

    void OnGUI() {
        win = (float)(position.width * 0.6);
        float w1 = (float)(win * 0.15);
        float w2 = (float)(win * 0.15);
        float w3 = (float)(win * 0.35);
        EditorGUILayout.LabelField("VRC Build Size Viewer", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Create a build of your world/avatar and click the button!", EditorStyles.label);
        if (GUILayout.Button("Read Build Log")) {
            buildLogFound = false;
            buildLogFound = getBuildSize();
        }
        if (buildLogFound) {
            if (uncompressedList != null && uncompressedList.Count != 0) {
                EditorGUILayout.LabelField("Total Compressed Build Size: " + totalSize);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Separator();
                EditorGUILayout.EndHorizontal();
                //EditorGUILayout.LabelField("Uncompressed Build Size by Category: ");
                foreach (string s in uncompressedList) {
                    EditorGUILayout.LabelField(s);
                }
            }
            if (buildObjectList != null && buildObjectList.Count != 0) {
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Separator();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Size%", GUILayout.Width(w1));
                EditorGUILayout.LabelField("Size", GUILayout.Width(w2));
                EditorGUILayout.LabelField("Path", GUILayout.Width(w3));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Separator();
                EditorGUILayout.EndHorizontal();
                foreach (BuildObject buildObject in buildObjectList) {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(buildObject.percent, GUILayout.Width(w1));
                    EditorGUILayout.LabelField(buildObject.size, GUILayout.Width(w2));
                    EditorGUILayout.LabelField(buildObject.path);
                    if(buildObject.path != "Resources/unity_builtin_extra") {
                        if (GUILayout.Button("Go", GUILayout.Width(w1))) {
                            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath(buildObject.path, typeof(UnityEngine.Object));
                            Selection.activeObject = obj;
                            EditorGUIUtility.PingObject(obj);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
        }
    }

    private bool getBuildSize() {
        try {
            using var stream = new FileStream(Application.consoleLogPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            if (ReadLastCompleteReport(reader))
                return true;

            Debug.LogWarning("No complete VRChat build size report was found in the Editor log.");
        }
        catch (System.Exception exception) when (exception is IOException ||
            exception is System.UnauthorizedAccessException || exception is System.ArgumentException) {
            Debug.LogWarning($"Could not read the Editor log: {exception.Message}");
        }
        return false;
    }

    private bool ReadLastCompleteReport(TextReader reader) {
        List<BuildObject> assets = null;
        List<string> categories = null;
        string compressedSize = null;
        bool found = false;
        string line;
        while ((line = reader.ReadLine()) != null) {
            if ((line.Contains("scene-") && line.Contains(".vrcw")) ||
                (line.Contains("avtr") && line.Contains(".prefab.unity3d"))) {
                categories = new List<string>();
                assets = null;
                compressedSize = null;
                continue;
            }
            if (categories == null)
                continue;

            if (compressedSize == null) {
                int separator = line.IndexOf(':');
                if (separator >= 0 && line.Contains("Compressed Size"))
                    compressedSize = line.Substring(separator + 1).Trim();
                continue;
            }
            if (assets == null) {
                if (line.Trim() == AssetListHeader)
                    assets = new List<BuildObject>();
                else
                    categories.Add(line);
                continue;
            }
            if (line.Trim() == ReportEnd) {
                totalSize = compressedSize;
                uncompressedList = categories;
                buildObjectList = assets;
                found = true;
                categories = null;
                continue;
            }

            var fields = line.Trim().Split(new[] { ' ', '\t' }, 4, System.StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 4 && fields[2].EndsWith("%", System.StringComparison.Ordinal)) {
                assets.Add(new BuildObject {
                    size = $"{fields[0]} {fields[1]}",
                    percent = fields[2],
                    path = fields[3].Trim()
                });
            }
        }
        return found;
    }
}
#endif
