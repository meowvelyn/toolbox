using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

public class VRChatIconAssigner : EditorWindow
{
    [SerializeField] private DefaultAsset iconRootDirectory;
    [SerializeField] private VRCExpressionsMenu rootMenu;

    private readonly Dictionary<string, Texture2D> icons = new();
    private readonly HashSet<string> assignedKeys = new();
    private readonly HashSet<Object> modifiedObjects = new();

    [MenuItem("Toolbox/Icon Assigner", false, 5)]
    private static void ShowWindow()
    {
        var window = GetWindow<VRChatIconAssigner>();
        window.titleContent = new GUIContent("VRChat Icon Assigner");
        window.Show();
    }

    private void OnGUI()
    {
        SerializedObject so = new SerializedObject(this);
        EditorGUILayout.PropertyField(so.FindProperty("iconRootDirectory"));
        EditorGUILayout.PropertyField(so.FindProperty("rootMenu"));
        so.ApplyModifiedProperties();

        if (GUILayout.Button("Assign Icons"))
        {
            AssignIcons();
        }
    }

    private void AssignIcons()
    {
        icons.Clear();
        assignedKeys.Clear();
        modifiedObjects.Clear();

        // Validate inputs
        if (iconRootDirectory == null)
        {
            Debug.LogError("Icon Root Directory is not assigned.");
            return;
        }

        string rootPath = AssetDatabase.GetAssetPath(iconRootDirectory);
        if (!AssetDatabase.IsValidFolder(rootPath))
        {
            Debug.LogError("Icon Root Directory is not a valid folder.");
            return;
        }

        if (rootMenu == null)
        {
            Debug.LogError("Root VRCExpressionsMenu is not assigned.");
            return;
        }

        // Mark root as modified
        modifiedObjects.Add(rootMenu);

        // Build icons map
        BuildIconsMap(rootPath);

        // Log available icons
        Debug.Log($"Available icons: {string.Join(", ", icons.Keys)}");

        // Traverse and assign folder-based icons first
        RecursiveAssignIcons(rootMenu, "", new HashSet<VRCExpressionsMenu>());

        // Then, assign loose icons (in root directory, no subfolder)
        var looseKeys = icons.Keys.Where(k => !k.Contains("/") && !assignedKeys.Contains(k)).ToList();
        foreach (var key in looseKeys)
        {
            AssignLooseIcon(rootMenu, key);
        }

        // Save changes
        foreach (var obj in modifiedObjects)
        {
            EditorUtility.SetDirty(obj);
            if (PrefabUtility.IsPartOfPrefabInstance(obj))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            }
        }
        AssetDatabase.SaveAssets();

        // Log unused
        var unused = icons.Keys.Where(k => !assignedKeys.Contains(k)).ToList();
        if (unused.Count > 0)
        {
            Debug.LogWarning($"Unused icons: {string.Join(", ", unused)}");
        }
        else
        {
            Debug.Log("All icons assigned.");
        }
    }

    private void BuildIconsMap(string rootPath)
    {
        string[] guidArray = AssetDatabase.FindAssets("t:Texture", new[] { rootPath });
        foreach (string guid in guidArray)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture != null)
            {
                string relPath = assetPath.Replace(rootPath + "/", "").Replace("\\", "/");
                string normalized = NormalizePath(relPath);
                icons[normalized] = texture;
            }
        }
    }

    private string NormalizePath(string path)
    {
        var segments = path.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            if (i == segments.Length - 1)
            {
                // Last segment: normalize name and remove extension if present
                var last = segments[i];
                var dotIndex = last.LastIndexOf('.');
                if (dotIndex > 0)
                {
                    var name = last.Substring(0, dotIndex);
                    segments[i] = NormalizeName(name);
                }
                else
                {
                    segments[i] = NormalizeName(last);
                }
            }
            else
            {
                // Folder segments: just normalize name
                segments[i] = NormalizeName(segments[i]);
            }
        }
        return string.Join("/", segments);
    }

    private string NormalizeName(string name)
    {
        // Remove leading digits followed by ". "
        var match = System.Text.RegularExpressions.Regex.Match(name, @"^\d+\.\s*(.+)");
        return match.Success ? match.Groups[1].Value : name;
    }

    private void RecursiveAssignIcons(VRCExpressionsMenu menu, string validKey, HashSet<VRCExpressionsMenu> ancestors)
    {
        if (!ancestors.Add(menu))
            return;

        string currentValidKey = validKey;

        SerializedObject serializedMenu = new SerializedObject(menu);
        SerializedProperty controlsProp = serializedMenu.FindProperty("controls");

        for (int i = 0; i < controlsProp.arraySize; i++)
        {
            SerializedProperty controlProp = controlsProp.GetArrayElementAtIndex(i);
            SerializedProperty nameProp = controlProp.FindPropertyRelative("name");
            string controlName = nameProp.stringValue;
            var normalizedCtrlName = NormalizeName(controlName);
            SerializedProperty typeProp = controlProp.FindPropertyRelative("type");

            string typeName = typeProp.enumNames[typeProp.enumValueIndex];

            if (typeName == "SubMenu")
            {
                // Assign to submenu if icon exists
                string submenuKey = normalizedCtrlName;
                if (icons.TryGetValue(submenuKey, out var submenuIcon))
                {
                    SerializedProperty iconProp = controlProp.FindPropertyRelative("icon");
                    iconProp.objectReferenceValue = submenuIcon;
                    assignedKeys.Add(submenuKey);

                    // Recurse with new validKey
                    SerializedProperty subMenuProp = controlProp.FindPropertyRelative("subMenu");
                    VRCExpressionsMenu subMenu = subMenuProp.objectReferenceValue as VRCExpressionsMenu;
                    if (subMenu != null)
                    {
                        modifiedObjects.Add(subMenu);
                        RecursiveAssignIcons(subMenu, normalizedCtrlName, ancestors);
                    }
                }
                else
                {
                    // Recurse with current validKey
                    SerializedProperty subMenuProp = controlProp.FindPropertyRelative("subMenu");
                    VRCExpressionsMenu subMenu = subMenuProp.objectReferenceValue as VRCExpressionsMenu;
                    if (subMenu != null)
                    {
                        modifiedObjects.Add(subMenu);
                        RecursiveAssignIcons(subMenu, currentValidKey, ancestors);
                    }
                }
            }
            else if (controlName.Equals("Enable", System.StringComparison.OrdinalIgnoreCase))
            {
                // Assign Enable toggle with current validKey
                if (!string.IsNullOrEmpty(currentValidKey))
                {
                    if (icons.TryGetValue(currentValidKey, out var enableIcon))
                    {
                        SerializedProperty iconProp = controlProp.FindPropertyRelative("icon");
                        iconProp.objectReferenceValue = enableIcon;
                        assignedKeys.Add(currentValidKey);
                    }
                }
            }
            else
            {
                // Assign other controls using current validKey
                if (!string.IsNullOrEmpty(currentValidKey))
                {
                    string controlKey = $"{currentValidKey}/{normalizedCtrlName}";
                    if (icons.TryGetValue(controlKey, out var ctrlIcon))
                    {
                        SerializedProperty iconProp = controlProp.FindPropertyRelative("icon");
                        iconProp.objectReferenceValue = ctrlIcon;
                        assignedKeys.Add(controlKey);
                    }
                }
            }
        }

        serializedMenu.ApplyModifiedProperties();
        ancestors.Remove(menu);
    }

    private void AssignLooseIcon(VRCExpressionsMenu menu, string key)
    {
        SerializedObject serializedMenu = new SerializedObject(menu);
        SerializedProperty controlsProp = serializedMenu.FindProperty("controls");

        for (int i = 0; i < controlsProp.arraySize; i++)
        {
            SerializedProperty controlProp = controlsProp.GetArrayElementAtIndex(i);
            SerializedProperty nameProp = controlProp.FindPropertyRelative("name");
            string controlName = nameProp.stringValue;
            var normalizedCtrlName = NormalizeName(controlName);

            if (normalizedCtrlName == key)
            {
                SerializedProperty iconProp = controlProp.FindPropertyRelative("icon");
                if (iconProp.objectReferenceValue == null)
                {
                    iconProp.objectReferenceValue = icons[key];
                    assignedKeys.Add(key);
                    modifiedObjects.Add(menu);
                }
            }

            // Recurse into submenus
            SerializedProperty typeProp = controlProp.FindPropertyRelative("type");
            if (typeProp.enumNames[typeProp.enumValueIndex] == "SubMenu")
            {
                SerializedProperty subMenuProp = controlProp.FindPropertyRelative("subMenu");
                VRCExpressionsMenu subMenu = subMenuProp.objectReferenceValue as VRCExpressionsMenu;
                if (subMenu != null)
                {
                    AssignLooseIcon(subMenu, key);
                }
            }
        }

        serializedMenu.ApplyModifiedProperties();
    }
}
