using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System.Linq; // Used for LINQ queries in GetCommonPrefix and other methods
using VRC.SDK3.Dynamics.PhysBone.Components; // VRChat PhysBone component for disabling physics during toggles

/// <summary>
/// Editor window for generating VRChat outfit toggle animations.
/// This tool allows users to create animations that toggle game objects (e.g., outfit parts) on or off,
/// while automatically disabling relevant PhysBones
/// </summary>
public class VRChatOutfitToggleGenerator : EditorWindow
{
    /// <summary>
    /// Selected avatar Animator component. Used as the root transform for calculating animation paths.
    /// </summary>
    [SerializeField] private Animator selectedAvatar;

    /// <summary>
    /// Directory where generated animation clips will be saved.
    /// </summary>
    [SerializeField] private DefaultAsset saveDirectory;

    /// <summary>
    /// Root game object of the outfit. All renderers under this root are auto-detected for animation entries.
    /// </summary>
    [SerializeField] private GameObject outfitRoot;

    /// <summary>
    /// Name of the subdirectory to create under saveDirectory for the generated animations.
    /// </summary>
    [SerializeField] private string outputSubdirectoryName = "";

    /// <summary>
    /// List of animation entries. Each entry defines an animation with toggles for multiple game objects.
    /// </summary>
    [SerializeField] private List<OutfitAnimationEntry> animationEntries = new List<OutfitAnimationEntry>();

    /// <summary>
    /// Key for saving/loading the saveDirectory path in PlayerPrefs.
    /// </summary>
    private string savePathKey = "VRChatOutfitToggle.SaveDirectory";

    /// <summary>
    /// SerializedObject for managing the window's serialized fields.
    /// </summary>
    private SerializedObject serializedObject;

    /// <summary>
    /// ReorderableList UI component for editing animation entries.
    /// </summary>
    private ReorderableList reorderableList;

    /// <summary>
    /// Scroll position for the GUI scroll view.
    /// </summary>
    private Vector2 scrollPos;

    /// <summary>
    /// Serializable class representing a single toggle item in an animation entry.
    /// </summary>
    [System.Serializable]
    public class ToggleItem
    {
        /// <summary>
        /// The game object to toggle on/off in the animation.
        /// </summary>
        public GameObject gameObject;

        /// <summary>
        /// Whether the game object should be enabled (true) or disabled (false) in the animation.
        /// </summary>
        public bool enabled;
    }

    /// <summary>
    /// Serializable class representing an animation entry with a name and list of toggles.
    /// </summary>
    [System.Serializable]
    public class OutfitAnimationEntry
    {
        /// <summary>
        /// Name of the animation clip to generate.
        /// </summary>
        public string animationName = "";

        /// <summary>
        /// List of game objects to toggle in this animation.
        /// </summary>
        public List<ToggleItem> toggles = new List<ToggleItem>();
    }

    /// <summary>
    /// Menu item to open the editor window. Placed under "Toolbox/Create Outfit Animations".
    /// </summary>
    [MenuItem("Toolbox/Create Animations", false, 1)]
    public static void ShowWindow()
    {
        VRChatOutfitToggleGenerator window = GetWindow<VRChatOutfitToggleGenerator>("Create Outfit Animations");
        window.minSize = new Vector2(400, 500);
        window.LoadPreferences();
    }

    /// <summary>
    /// Called when the window is enabled. Initializes serialized objects and the reorderable list.
    /// </summary>
    private void OnEnable()
    {
        serializedObject = new SerializedObject(this);
        reorderableList = new ReorderableList(serializedObject, serializedObject.FindProperty("animationEntries"), true, true, true, true);
        reorderableList.drawHeaderCallback = DrawHeaderCallback;
        reorderableList.drawElementCallback = DrawListElement;
        reorderableList.elementHeightCallback = GetElementHeight;
        reorderableList.onAddCallback = OnAddListElement;
        LoadPreferences();
    }

    /// <summary>
    /// Called when the window is disabled. Saves preferences.
    /// </summary>
    private void OnDisable()
    {
        SavePreferences();
    }

    /// <summary>
    /// Loads the save directory path from PlayerPrefs.
    /// </summary>
    private void LoadPreferences()
    {
        string path = PlayerPrefs.GetString(savePathKey, "");
        if (!string.IsNullOrEmpty(path))
        {
            saveDirectory = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
        }
    }

    /// <summary>
    /// Saves the save directory path to PlayerPrefs.
    /// </summary>
    private void SavePreferences()
    {
        if (saveDirectory != null)
        {
            string path = AssetDatabase.GetAssetPath(saveDirectory);
            PlayerPrefs.SetString(savePathKey, path);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Draws the header for the reorderable list and handles drag-and-drop to create new animation entries.
    /// </summary>
    private void DrawHeaderCallback(Rect rect)
    {
        EditorGUI.LabelField(rect, "Animation Entries");
        Event evt = Event.current;
        if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && rect.Contains(evt.mousePosition))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (Object draggedObj in DragAndDrop.objectReferences)
                {
                    if (draggedObj is GameObject go)
                    {
                        animationEntries.Add(new OutfitAnimationEntry
                        {
                            animationName = go.name,
                            toggles = new List<ToggleItem> { new ToggleItem { gameObject = go, enabled = false } }
                        });
                    }
                }
                serializedObject.ApplyModifiedProperties();
                Repaint();
                evt.Use();
            }
            else
            {
                evt.Use();
            }
        }
    }

    /// <summary>
    /// Called when a new list element is added to the reorderable list. Initializes default values.
    /// </summary>
    private void OnAddListElement(ReorderableList list)
    {
        list.serializedProperty.InsertArrayElementAtIndex(list.serializedProperty.arraySize);
        SerializedProperty newElement = list.serializedProperty.GetArrayElementAtIndex(list.serializedProperty.arraySize - 1);
        newElement.FindPropertyRelative("animationName").stringValue = "";
        SerializedProperty toggles = newElement.FindPropertyRelative("toggles");
        toggles.ClearArray();
        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>
    /// Draws each element in the reorderable list, including animation name, toggles, and controls.
    /// Also handles drag-and-drop for adding toggles to existing entries.
    /// </summary>
    private void DrawListElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        SerializedProperty element = reorderableList.serializedProperty.GetArrayElementAtIndex(index);
        SerializedProperty nameProp = element.FindPropertyRelative("animationName");
        SerializedProperty togglesProp = element.FindPropertyRelative("toggles");

        Rect currentRect = rect;
        currentRect.height = EditorGUIUtility.singleLineHeight;
    
        EditorGUI.PropertyField(currentRect, nameProp, new GUIContent("Animation Name:"));
        currentRect.y += EditorGUIUtility.singleLineHeight + 5;

        // Draw each toggle manually with GO, checkbox, and remove button
        for (int i = 0; i < togglesProp.arraySize; i++)
        {
            SerializedProperty toggleProp = togglesProp.GetArrayElementAtIndex(i);
            SerializedProperty goProp = toggleProp.FindPropertyRelative("gameObject");
            SerializedProperty enabledProp = toggleProp.FindPropertyRelative("enabled");

            Rect lineRect = new Rect(currentRect.x, currentRect.y, currentRect.width, EditorGUIUtility.singleLineHeight);

            // Layout: GO field | checkbox | remove button
            const float removeButtonWidth = 70f;
            const float checkboxSectionWidth = 35f; // Space for toggle

            Rect goRect = lineRect;
            goRect.width -= removeButtonWidth + checkboxSectionWidth + 10; // Space

            Rect checkboxSectionRect = new Rect(goRect.xMax + 5, lineRect.y, checkboxSectionWidth, lineRect.height);
            Rect removeRect = new Rect(checkboxSectionRect.xMax + 5, lineRect.y, removeButtonWidth, lineRect.height);

            EditorGUI.PropertyField(goRect, goProp, GUIContent.none);
            enabledProp.boolValue = EditorGUI.Toggle(checkboxSectionRect, enabledProp.boolValue);

            if (GUI.Button(removeRect, "Remove"))
            {
                togglesProp.DeleteArrayElementAtIndex(i);
                i--; // Adjust index since we removed one
            }

            currentRect.y += EditorGUIUtility.singleLineHeight + 2;
        }

        // Add button below the list
        Rect addRect = new Rect(currentRect.x + 10, currentRect.y, 120f, EditorGUIUtility.singleLineHeight);
        if (GUI.Button(addRect, "Add GameObject"))
        {
            togglesProp.InsertArrayElementAtIndex(togglesProp.arraySize);
            SerializedProperty newToggle = togglesProp.GetArrayElementAtIndex(togglesProp.arraySize - 1);
            newToggle.FindPropertyRelative("gameObject").objectReferenceValue = null;
            newToggle.FindPropertyRelative("enabled").boolValue = false;
        }
        currentRect.y += EditorGUIUtility.singleLineHeight + 5;

        // Handle drag and drop for adding toggles to this entry
        Event evt = Event.current;
        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            Rect dropRect = new Rect(rect.x, rect.y, rect.width, currentRect.y - rect.y);
            if (dropRect.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (Object draggedObj in DragAndDrop.objectReferences)
                    {
                        if (draggedObj is GameObject go)
                        {
                            togglesProp.InsertArrayElementAtIndex(togglesProp.arraySize);
                            SerializedProperty newToggle = togglesProp.GetArrayElementAtIndex(togglesProp.arraySize - 1);
                            newToggle.FindPropertyRelative("gameObject").objectReferenceValue = go;
                            newToggle.FindPropertyRelative("enabled").boolValue = false;
                        }
                    }
                    serializedObject.ApplyModifiedProperties();
                    evt.Use();
                }
                else
                {
                    evt.Use();
                }
            }
        }
    }

    /// <summary>
    /// Calculates the height required for drawing a list element.
    /// </summary>
    private float GetElementHeight(int index)
    {
        SerializedProperty element = reorderableList.serializedProperty.GetArrayElementAtIndex(index);
        SerializedProperty togglesProp = element.FindPropertyRelative("toggles");
        float height = EditorGUIUtility.singleLineHeight + 5; // Name
        height += togglesProp.arraySize * (EditorGUIUtility.singleLineHeight + 2) + 5; // Toggles
        height += EditorGUIUtility.singleLineHeight + 5; // Add button
        return height;
    }

    /// <summary>
    /// Finds the common prefix among a collection of strings. Used for stripping prefixes from renderer names during auto-detection.
    /// </summary>
    private string GetCommonPrefix(IEnumerable<string> strings)
    {
        if (!strings.Any()) return "";
        string prefix = strings.First();
        foreach (var str in strings.Skip(1))
        {
            int i = 0;
            for (; i < prefix.Length && i < str.Length; i++)
            {
                if (prefix[i] != str[i]) break;
            }
            prefix = prefix.Substring(0, i);
            if (string.IsNullOrEmpty(prefix)) break;
        }
        return prefix;
    }

    /// <summary>
    /// Main GUI drawing method. Handles all fields and controls.
    /// </summary>
    private void OnGUI()
    {
        scrollPos = GUILayout.BeginScrollView(scrollPos);

        serializedObject.Update();

        EditorGUILayout.LabelField("Avatar Selection", EditorStyles.boldLabel);
        selectedAvatar = (Animator)EditorGUILayout.ObjectField("Select Avatar", selectedAvatar, typeof(Animator), true);

        if (selectedAvatar == null)
        {
            outfitRoot = null;
            GUILayout.EndScrollView();
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Save Directory", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        saveDirectory = (DefaultAsset)EditorGUILayout.ObjectField("Select Save Folder", saveDirectory, typeof(DefaultAsset), false);
        if (EditorGUI.EndChangeCheck())
        {
            SavePreferences();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Outfit Root", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        outfitRoot = (GameObject)EditorGUILayout.ObjectField("Select Outfit Root GameObject", outfitRoot, typeof(GameObject), true);
        bool outfitRootChanged = EditorGUI.EndChangeCheck();
        if (outfitRootChanged && outfitRoot != null)
        {
            outputSubdirectoryName = outfitRoot.name;
            animationEntries.Clear();
            AutoDetectRenderers();
        }

        if (outfitRoot != null && !outfitRoot.transform.IsChildOf(selectedAvatar.transform))
        {
            EditorGUILayout.HelpBox("Outfit Root must be a child of the selected Avatar!", MessageType.Warning);
        }

        EditorGUILayout.Space();
        outputSubdirectoryName = EditorGUILayout.TextField("Output Subdirectory", outputSubdirectoryName);

        EditorGUILayout.Space();
        reorderableList.DoLayoutList();

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate Animations", GUILayout.Height(40)))
        {
            GenerateAnimations();
        }

        GUILayout.EndScrollView();
    }

    /// <summary>
    /// Auto-detects renderers under the outfit root and creates animation entries for each.
    /// Strips common prefixes from names for cleaner animation names.
    /// </summary>
    private void AutoDetectRenderers()
    {
        var renderers = outfitRoot.GetComponentsInChildren<Renderer>(true);
        var rendererNames = renderers.Select(r => r.gameObject.name);
        string prefix = GetCommonPrefix(rendererNames);
        bool changed = false;
        foreach (var renderer in renderers)
        {
            GameObject go = renderer.gameObject;
            string animationName = go.name;
            if (animationName.StartsWith(prefix))
            {
                animationName = animationName.Substring(prefix.Length);
            }
            animationEntries.Add(new OutfitAnimationEntry
            {
                animationName = animationName,
                toggles = new List<ToggleItem> { new ToggleItem { gameObject = go, enabled = false } }
            });
            changed = true;
        }

        if (changed)
        {
            serializedObject.Update();
            Repaint();
        }
    }

    /// <summary>
    /// Gets all transforms in a PhysBone's hierarchy, including root and children.
    /// </summary>
    private IEnumerable<Transform> GetPhysBoneHierarchy(VRCPhysBone pb)
    {
        Transform root = pb.rootTransform != null ? pb.rootTransform : pb.transform;
        var transforms = new List<Transform> { root };
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            transforms.Add(child);
        }
        return transforms;
    }

    /// <summary>
    /// Generates animation clips based on the animation entries.
    /// Handles toggling game objects and disabling relevant PhysBones.
    /// </summary>
    private void GenerateAnimations()
    {
        if (selectedAvatar == null || saveDirectory == null || string.IsNullOrEmpty(outputSubdirectoryName) || animationEntries.Count == 0)
        {
            Debug.LogError("Error: Ensure Avatar, Save Directory, Subdirectory, and at least one Animation Entry are set.");
            return;
        }

        string basePath = AssetDatabase.GetAssetPath(saveDirectory);
        if (outfitRoot == null || !outfitRoot.transform.IsChildOf(selectedAvatar.transform) ||
            animationEntries.SelectMany(entry => entry.toggles).Any(toggle =>
                toggle.gameObject != null && !toggle.gameObject.transform.IsChildOf(selectedAvatar.transform)))
        {
            Debug.LogError("The outfit root and all toggle targets must be inside the selected avatar.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(basePath) ||
            (basePath != "Assets" && !basePath.StartsWith("Assets/", System.StringComparison.Ordinal)) ||
            !IsValidFileName(outputSubdirectoryName) || animationEntries.Any(entry =>
                !string.IsNullOrEmpty(entry.animationName) && !IsValidFileName(entry.animationName)))
        {
            Debug.LogError("Choose a folder under Assets and use valid folder and animation names.");
            return;
        }

        string subDirPath = $"{basePath}/{outputSubdirectoryName}";
        if (!AssetDatabase.IsValidFolder(subDirPath))
        {
            string guid = AssetDatabase.CreateFolder(basePath, outputSubdirectoryName);
            subDirPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(subDirPath))
            {
                Debug.LogError("Could not create the animation output folder.");
                return;
            }
        }

        foreach (var entry in animationEntries)
        {
            if (string.IsNullOrEmpty(entry.animationName) || entry.toggles.Count == 0) continue;

            // Collect unique bones from toggles that are disabled, checking for weight information
            HashSet<Transform> uniqueBones = new HashSet<Transform>();
            foreach (var toggle in entry.toggles.Where(t => !t.enabled))
            {
                var go = toggle.gameObject;
                if (go == null) continue;
                foreach (var renderer in go.GetComponents<Renderer>())
                {
                    if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                    {
                        var bones = smr.bones;
                        var boneWeights = smr.sharedMesh.boneWeights;
                        for (int i = 0; i < bones.Length; i++)
                        {
                            if (bones[i] == null) continue;
                            if (i >= smr.sharedMesh.bindposes.Length) continue;

                            bool hasWeight = false;
                            for (int v = 0; v < boneWeights.Length; v++)
                            {
                                var bw = boneWeights[v];
                                if ((bw.boneIndex0 == i && bw.weight0 > 0) ||
                                    (bw.boneIndex1 == i && bw.weight1 > 0) ||
                                    (bw.boneIndex2 == i && bw.weight2 > 0) ||
                                    (bw.boneIndex3 == i && bw.weight3 > 0))
                                {
                                    hasWeight = true;
                                    break;
                                }
                            }
                            if (hasWeight)
                            {
                                uniqueBones.Add(bones[i]);
                            }
                        }
                    }
                }
            }

            // Detect PhysBones to disable based on overlapping bones
            List<VRCPhysBone> physBonesToDisable = new List<VRCPhysBone>();
            foreach (var pb in outfitRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {
                var pbHierarchy = GetPhysBoneHierarchy(pb);
                if (uniqueBones.Any(bone => pbHierarchy.Contains(bone)))
                {
                    physBonesToDisable.Add(pb);
                }
            }

            // Create the animation clip
            AnimationClip clip = new AnimationClip();
            clip.name = entry.animationName;

            // Set PhysBones to disabled
            foreach (var pb in physBonesToDisable)
            {
                string path = AnimationUtility.CalculateTransformPath(pb.transform, selectedAvatar.transform);
                EditorCurveBinding binding = new EditorCurveBinding { path = path, type = typeof(VRCPhysBone), propertyName = "m_Enabled" };
                AnimationCurve curve = new AnimationCurve(new Keyframe(0, 0f)); // Disable
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

            // Set game object active states based on toggles
            foreach (var toggle in entry.toggles)
            {
                if (toggle.gameObject == null) continue;
                string path = AnimationUtility.CalculateTransformPath(toggle.gameObject.transform, selectedAvatar.transform);
                EditorCurveBinding binding = new EditorCurveBinding { path = path, type = typeof(GameObject), propertyName = "m_IsActive" };
                AnimationCurve curve = new AnimationCurve(new Keyframe(0, toggle.enabled ? 1f : 0f));
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

            // Save the clip and log
            string clipPath = $"{subDirPath}/{entry.animationName}.anim";
            AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath(clipPath));
            Debug.Log($"Animation <color=#00FFFF><b>{entry.animationName}</b></color> created", clip);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"<color=#00FF00>Success:</color> Animations generated in <color=#00FFFF><b>{subDirPath}</b></color>");
    }
    private static bool IsValidFileName(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && name == name.Trim().TrimEnd('.') &&
            name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;
    }
}
