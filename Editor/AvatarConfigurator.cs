using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Linq;
using System.Reflection;
using VRC.SDK3.Avatars.Components;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.ScriptableObjects;
using System.Collections.Generic;
using AnimatorController = UnityEditor.Animations.AnimatorController;

public class AvatarConfigurator : EditorWindow
{
    private const string PREFS_KEY_SETTINGS_PREFAB = "AvatarConfigurator_SettingsPrefab";
    private const string PREFS_KEY_FX_CONTROLLER = "AvatarConfigurator_FXController";
    private const string PREFS_KEY_GESTURE_CONTROLLER = "AvatarConfigurator_GestureController";
    private const string PREFS_KEY_EXPRESSION_MENU = "AvatarConfigurator_ExpressionMenu";
    private const string PREFS_KEY_EXPRESSION_PARAMETERS = "AvatarConfigurator_ExpressionParameters";
    private const string PREFS_KEY_ADDITIONAL_PREFABS = "AvatarConfigurator_AdditionalPrefabs";

    [MenuItem("Toolbox/Quick Configure Avatar", false, 0)]
    static void ShowWindow()
    {
        GetWindow<AvatarConfigurator>("Avatar Configurator");
    }

    private VRCAvatarDescriptor selectedAvatar;
    private GameObject settingsPrefab;
    private AnimatorController fxController;
    private AnimatorController gestureController;
    private VRCExpressionsMenu expressionsMenu;
    private VRCExpressionParameters expressionParameters;
    private ReorderableList additionalPrefabsList;
    private List<GameObject> additionalPrefabs = new List<GameObject>();

    void OnEnable()
    {
        LoadPreferences();

        additionalPrefabsList = new ReorderableList(additionalPrefabs, typeof(GameObject), true, true, true, true);
        additionalPrefabsList.drawHeaderCallback = (Rect rect) => { EditorGUI.LabelField(rect, "Additional Prefabs (Children)"); };
        additionalPrefabsList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) => {
            rect.y += 2;
            rect.height = EditorGUIUtility.singleLineHeight;
            additionalPrefabs[index] = (GameObject)EditorGUI.ObjectField(rect, additionalPrefabs[index], typeof(GameObject), false);
        };
        additionalPrefabsList.onAddCallback = (ReorderableList list) => {
            additionalPrefabs.Add(null);
            SavePreferences();
        };
        additionalPrefabsList.onRemoveCallback = (ReorderableList list) => {
            additionalPrefabs.RemoveAt(list.index);
            SavePreferences();
        };

        // Automatically find the first VRCAvatarDescriptor in the scene
        var avatars = FindObjectsOfType<VRCAvatarDescriptor>();
        if (avatars.Length > 0)
        {
            selectedAvatar = avatars[0];
        }
        else
        {
            selectedAvatar = null;
        }
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Select VRChat Avatar", EditorStyles.boldLabel);
        selectedAvatar = EditorGUILayout.ObjectField("Avatar Descriptor", selectedAvatar, typeof(VRCAvatarDescriptor), true) as VRCAvatarDescriptor;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Configuration Assets", EditorStyles.boldLabel);
        settingsPrefab = (GameObject)EditorGUILayout.ObjectField("Settings Prefab", settingsPrefab, typeof(GameObject), false);
        if (settingsPrefab != null) EditorPrefs.SetString(PREFS_KEY_SETTINGS_PREFAB, AssetDatabase.GetAssetPath(settingsPrefab));
        else EditorPrefs.SetString(PREFS_KEY_SETTINGS_PREFAB, "");

        fxController = (AnimatorController)EditorGUILayout.ObjectField("FX Controller", fxController, typeof(AnimatorController), false);
        if (fxController != null) EditorPrefs.SetString(PREFS_KEY_FX_CONTROLLER, AssetDatabase.GetAssetPath(fxController));
        else EditorPrefs.SetString(PREFS_KEY_FX_CONTROLLER, "");

        gestureController = (AnimatorController)EditorGUILayout.ObjectField("Gesture Controller", gestureController, typeof(AnimatorController), false);
        if (gestureController != null) EditorPrefs.SetString(PREFS_KEY_GESTURE_CONTROLLER, AssetDatabase.GetAssetPath(gestureController));
        else EditorPrefs.SetString(PREFS_KEY_GESTURE_CONTROLLER, "");

        expressionsMenu = (VRCExpressionsMenu)EditorGUILayout.ObjectField("Expressions Menu", expressionsMenu, typeof(VRCExpressionsMenu), false);
        if (expressionsMenu != null) EditorPrefs.SetString(PREFS_KEY_EXPRESSION_MENU, AssetDatabase.GetAssetPath(expressionsMenu));
        else EditorPrefs.SetString(PREFS_KEY_EXPRESSION_MENU, "");

        expressionParameters = (VRCExpressionParameters)EditorGUILayout.ObjectField("Expression Parameters", expressionParameters, typeof(VRCExpressionParameters), false);
        if (expressionParameters != null) EditorPrefs.SetString(PREFS_KEY_EXPRESSION_PARAMETERS, AssetDatabase.GetAssetPath(expressionParameters));
        else EditorPrefs.SetString(PREFS_KEY_EXPRESSION_PARAMETERS, "");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Additional Prefabs (added as children)", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        additionalPrefabsList.DoLayoutList();
        if (EditorGUI.EndChangeCheck())
        {
            SavePreferences();
        }

        if (GUILayout.Button("Configure Avatar"))
        {
            if (ValidateInputs())
            {
                ConfigureAvatar();
            }
        }
    }

    void LoadPreferences()
    {
        string settingsPath = EditorPrefs.GetString(PREFS_KEY_SETTINGS_PREFAB, "");
        if (!string.IsNullOrEmpty(settingsPath)) settingsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(settingsPath);

        string fxPath = EditorPrefs.GetString(PREFS_KEY_FX_CONTROLLER, "");
        if (!string.IsNullOrEmpty(fxPath)) fxController = AssetDatabase.LoadAssetAtPath<AnimatorController>(fxPath);

        string gesturePath = EditorPrefs.GetString(PREFS_KEY_GESTURE_CONTROLLER, "");
        if (!string.IsNullOrEmpty(gesturePath)) gestureController = AssetDatabase.LoadAssetAtPath<AnimatorController>(gesturePath);

        string menuPath = EditorPrefs.GetString(PREFS_KEY_EXPRESSION_MENU, "");
        if (!string.IsNullOrEmpty(menuPath)) expressionsMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(menuPath);

        string paramsPath = EditorPrefs.GetString(PREFS_KEY_EXPRESSION_PARAMETERS, "");
        if (!string.IsNullOrEmpty(paramsPath)) expressionParameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(paramsPath);

        string prefabsJson = EditorPrefs.GetString(PREFS_KEY_ADDITIONAL_PREFABS, "");
        if (!string.IsNullOrEmpty(prefabsJson))
        {
            SerializableList wrapper = JsonUtility.FromJson<SerializableList>(prefabsJson);
            additionalPrefabs = wrapper.items != null ? wrapper.items.Select(path => string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path)).ToList() : new List<GameObject>();
        }
        else
        {
            additionalPrefabs = new List<GameObject>();
        }
    }

    void SavePreferences()
    {
        SerializableList wrapper = new SerializableList();
        wrapper.items = additionalPrefabs.Select(prefab => AssetDatabase.GetAssetPath(prefab)).ToList();
        EditorPrefs.SetString(PREFS_KEY_ADDITIONAL_PREFABS, JsonUtility.ToJson(wrapper));
    }

    bool ValidateInputs()
    {
        if (settingsPrefab == null) { EditorUtility.DisplayDialog("Error", "Settings Prefab is required.", "OK"); return false; }
        if (fxController == null) { EditorUtility.DisplayDialog("Error", "FX Controller is required.", "OK"); return false; }
        if (gestureController == null) { EditorUtility.DisplayDialog("Error", "Gesture Controller is required.", "OK"); return false; }
        if (expressionsMenu == null) { EditorUtility.DisplayDialog("Error", "Expressions Menu is required.", "OK"); return false; }
        if (expressionParameters == null) { EditorUtility.DisplayDialog("Error", "Expression Parameters is required.", "OK"); return false; }
        return true;
    }

    void ConfigureAvatar()
    {
        if (selectedAvatar == null)
        {
            Debug.LogError("No avatar selected.");
            return;
        }

        GameObject avatarRoot = selectedAvatar.gameObject;
        Animator animator = avatarRoot.GetComponent<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            Debug.LogError("Animator component missing or no humanoid avatar set.");
            return;
        }

        // Find Armature (parent of hips to handle custom names)
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null)
        {
            Debug.LogError("Hips bone not found in humanoid rig.");
            return;
        }
        Transform armature = hips.parent;
        if (armature == null)
        {
            Debug.LogError("Armature parent not found for hips.");
            return;
        }

        var layers = selectedAvatar.baseAnimationLayers;
        int gestureIndex = System.Array.FindIndex(layers ?? System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>(),
            layer => layer.type == VRCAvatarDescriptor.AnimLayerType.Gesture);
        int fxIndex = System.Array.FindIndex(layers ?? System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>(),
            layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX);
        if (gestureIndex < 0 || fxIndex < 0)
        {
            Debug.LogError("Initialize the avatar descriptor's Gesture and FX playable layers before configuring it.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Configure VRChat Avatar");
        int undoGroup = Undo.GetCurrentGroup();

        // Create Light Anchor or reuse existing
        GameObject lightAnchor = null;
        foreach (Transform child in armature)
        {
            if (child.name == "Light Anchor")
            {
                lightAnchor = child.gameObject;
                break;
            }
        }
        if (lightAnchor == null)
        {
            lightAnchor = new GameObject("Light Anchor");
            lightAnchor.transform.SetParent(armature);
            Undo.RegisterCreatedObjectUndo(lightAnchor, "Create Light Anchor");
        }
        Undo.RecordObject(lightAnchor.transform, "Position Light Anchor");
        lightAnchor.transform.position = armature.position + Vector3.up;

        // Copy monobehaviours from settings prefab to avatar root, only blocking duplicates if values are identical
        Component[] components = settingsPrefab.GetComponents<Component>();
        foreach (Component comp in components)
        {
            if (comp == null || comp is Transform || comp is Animator || comp is VRCAvatarDescriptor)
                continue;
            System.Type compType = comp.GetType();
            bool alreadyExists = false;
            foreach (Component existing in avatarRoot.GetComponents(compType))
            {
                if (AreComponentsIdentical(comp, existing))
                {
                    alreadyExists = true;
                    break;
                }
            }
            if (!alreadyExists)
            {
                CopyComponent(comp, avatarRoot);
            }
            else
            {
                Debug.LogWarning($"Avatar root already has an identical {compType.Name} component. Skipping duplicate.");
            }
        }

        // Instantiate additional prefabs as children
        foreach (GameObject prefab in additionalPrefabs)
        {
            if (prefab != null && PrefabUtility.IsPartOfPrefabAsset(prefab) &&
                !avatarRoot.transform.Cast<Transform>().Any(child =>
                    PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == prefab))
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, avatarRoot.transform);
                if (instance != null)
                    Undo.RegisterCreatedObjectUndo(instance, $"Add {prefab.name} as child");
            }
        }

        // Ensure LightAnchorComponent is added only if not present
        System.Type lightAnchorType = GetTypeFromGUID("560fdafd46c74b2db6422fdf0e7f2363");
        if (lightAnchorType != null && avatarRoot.GetComponent(lightAnchorType) == null)
        {
            LightAnchorComponent(avatarRoot, armature, hips, lightAnchor);
        }
        else if (lightAnchorType != null)
        {
            Debug.LogWarning("Light Anchor Component already present. Updating references.");
            // Optionally update the existing component
            Component existingComp = avatarRoot.GetComponent(lightAnchorType);
            SerializeLightAnchor(existingComp, armature, hips, lightAnchor);
        }
        else
        {
            Debug.LogError("Failed to get Light Anchor Component type.");
        }

        Undo.RecordObject(selectedAvatar, "Modify Avatar Descriptor");
        layers[gestureIndex].isDefault = false;
        layers[gestureIndex].animatorController = gestureController;
        layers[fxIndex].isDefault = false;
        layers[fxIndex].animatorController = fxController;
        selectedAvatar.customizeAnimationLayers = true;
        selectedAvatar.customExpressions = true;
        selectedAvatar.expressionsMenu = expressionsMenu;
        selectedAvatar.expressionParameters = expressionParameters;
        PrefabUtility.RecordPrefabInstancePropertyModifications(selectedAvatar);

        Undo.CollapseUndoOperations(undoGroup);
    }

    Component CopyComponent(Component original, GameObject destination)
    {
        System.Type type = original.GetType();
        Component copy = Undo.AddComponent(destination, type);
        if (copy == null)
            return null;

        Undo.RecordObject(copy, $"Copy {type.Name} Settings");
        EditorUtility.CopySerialized(original, copy);
        PrefabUtility.RecordPrefabInstancePropertyModifications(copy);

        return copy;
    }

    bool AreComponentsIdentical(Component a, Component b)
    {
        if (a == null || b == null) return false;
        if (a.GetType() != b.GetType()) return false;
        string jsonA = JsonUtility.ToJson(a);
        string jsonB = JsonUtility.ToJson(b);
        return jsonA == jsonB;
    }

    void LightAnchorComponent(GameObject root, Transform armature, Transform hips, GameObject lightAnchor)
    {
        System.Type componentType = GetTypeFromGUID("560fdafd46c74b2db6422fdf0e7f2363");
        if (componentType != null)
        {
            Component comp = root.AddComponent(componentType);
            Undo.RegisterCreatedObjectUndo(comp, "Add Light Anchor Component");
            SerializeLightAnchor(comp, armature, hips, lightAnchor);
        }
        else
        {
            Debug.LogError("Failed to get type for GUID 560fdafd46c74b2db6422fdf0e7f2363.");
        }
    }

    void SerializeLightAnchor(Component comp, Transform armature, Transform hips, GameObject lightAnchor)
    {
        SerializedObject so = new SerializedObject(comp);
        Undo.RecordObject(comp, "Modify Light Anchor Component");

        if (so != null)
        {
            var inheritProbeAnchor = so.FindProperty("InheritProbeAnchor");
            var inheritBounds = so.FindProperty("InheritBounds");
            if (inheritProbeAnchor != null) inheritProbeAnchor.boolValue = true;
            if (inheritBounds != null) inheritBounds.boolValue = true;

            var probeAnchor = so.FindProperty("ProbeAnchor");
            if (probeAnchor != null)
            {
                var referencePath = probeAnchor.FindPropertyRelative("referencePath");
                var targetObject = probeAnchor.FindPropertyRelative("targetObject");
                if (referencePath != null) referencePath.stringValue = FullPath(lightAnchor.transform, comp.transform.root);
                if (targetObject != null) targetObject.objectReferenceValue = lightAnchor;
            }

            var rootBone = so.FindProperty("RootBone");
            if (rootBone != null)
            {
                var referencePath = rootBone.FindPropertyRelative("referencePath");
                var targetObject = rootBone.FindPropertyRelative("targetObject");
                if (referencePath != null) referencePath.stringValue = FullPath(hips, comp.transform.root);
                if (targetObject != null) targetObject.objectReferenceValue = hips;
            }

            var bounds = so.FindProperty("Bounds");
            if (bounds != null)
            {
                var center = bounds.FindPropertyRelative("m_Center");
                var extent = bounds.FindPropertyRelative("m_Extent");
                if (center != null) center.vector3Value = Vector3.zero;
                if (extent != null) extent.vector3Value = Vector3.one;
            }

            so.ApplyModifiedProperties();
        }
    }

    System.Type GetTypeFromGUID(string guid)
    {
        // Try loading MonoScript from AssetDatabase
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (!string.IsNullOrEmpty(path))
        {
            MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (monoScript != null)
            {
                return monoScript.GetClass();
            }
        }

        // Fallback: Use internal method (may not work in all Unity versions)
        var method = typeof(UnityEditorInternal.InternalEditorUtility).GetMethod("GetScriptTypeFromGUID", BindingFlags.NonPublic | BindingFlags.Static);
        if (method != null)
        {
            return (System.Type)method.Invoke(null, new object[] { guid });
        }

        return null;
    }

    string FullPath(Transform obj, Transform root)
    {
        string path = obj.name;
        while (obj.parent != null && obj.parent != root)
        {
            obj = obj.parent;
            path = obj.name + "/" + path;
        }
        if (obj.parent != root) return "";
        return path;
    }

    [System.Serializable]
    private class SerializableList
    {
        public List<string> items;
    }
}
