#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using static AvatarToolbox.MenuHandler;
using static AvatarToolbox.ParameterHandler;
using System.IO;
using System;
using System.Text.RegularExpressions;

[System.Serializable]
public class ParameterCondition
{
    public string parameterName;
    public AnimatorConditionMode conditionMode = AnimatorConditionMode.Equals;
    public float threshold = 0f;
}

public class AddBoolLayer : EditorWindow
{
    // Avatar components
    private VRCAvatarDescriptor avatarDescriptor;
    private AnimatorController controller;
    private bool addToVRCMenu = false;
    private VRCExpressionsMenu vrcMenu;
    private VRCExpressionParameters vrcParameters;
    private string submenuName = "Bools";
    private string prefix = "bools/";

    // Selecting animations
    [SerializeField]
    private List<AnimationClip> animationClipsList = new List<AnimationClip>();

    // Multiple extra conditions
    [SerializeField]
    private List<ParameterCondition> extraConditions = new List<ParameterCondition>();

    // Quick Outfits fields
    private bool quickOutfits = false;
    private DefaultAsset animationsDirectory;
    private string selectedIntParameter;

    private Vector2 scrollPosition;

    [MenuItem("Toolbox/Create Bool Layer", false, 3)]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow<AddBoolLayer>("Create Bool Layers");
    }

    private SerializedObject serializedObject;
    private void OnEnable()
    {
        serializedObject = new SerializedObject(this);
    }

    private void OnGUI()
    {
        avatarDescriptor = EditorGUILayout.ObjectField("Avatar Descriptor", avatarDescriptor, typeof(VRCAvatarDescriptor), true) as VRCAvatarDescriptor;
        controller = GetFxController(avatarDescriptor);
        vrcMenu = avatarDescriptor != null ? avatarDescriptor.expressionsMenu : null;
        vrcParameters = avatarDescriptor != null ? avatarDescriptor.expressionParameters : null;
        if (avatarDescriptor != null && controller == null)
            EditorGUILayout.LabelField("No editable animator controller found on the FX playable layer.");

        GUI.enabled = avatarDescriptor != null;
        addToVRCMenu = EditorGUILayout.Toggle("Add to VRChat Menu", addToVRCMenu);
        if (addToVRCMenu && avatarDescriptor != null)
        {
            if (vrcMenu == null)
                EditorGUILayout.LabelField("No Expressions Menu found.");
            if (vrcParameters == null)
                EditorGUILayout.LabelField("No Expression Parameters found.");
            submenuName = EditorGUILayout.TextField("Submenu Name", submenuName);
        }

        quickOutfits = EditorGUILayout.Toggle("Quick Outfits", quickOutfits);

        if (!quickOutfits)
        {
            prefix = EditorGUILayout.TextField("Prefix", prefix);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Extra Conditions", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("If the condition is a bool:");
            EditorGUILayout.LabelField("Use \'If 0\' for true, and \'If Not 0\' for false");
            if (controller != null)
            {
                string[] parameterOptions = controller.parameters.Select(p => p.name).ToArray();
                for (int i = 0; i < extraConditions.Count; i++)
                {
                    EditorGUILayout.BeginVertical("box");
                    if (parameterOptions.Length > 0)
                    {
                        int currentIndex = System.Array.IndexOf(parameterOptions, extraConditions[i].parameterName);
                        if (currentIndex < 0) currentIndex = 0;
                        currentIndex = EditorGUILayout.Popup("Parameter", currentIndex, parameterOptions);
                        extraConditions[i].parameterName = parameterOptions[currentIndex];
                    }
                    else
                    {
                        extraConditions[i].parameterName = EditorGUILayout.TextField("Parameter", extraConditions[i].parameterName);
                    }
                    extraConditions[i].conditionMode = (AnimatorConditionMode)EditorGUILayout.EnumPopup("Condition Mode", extraConditions[i].conditionMode);
                    extraConditions[i].threshold = EditorGUILayout.FloatField("Threshold", extraConditions[i].threshold);
                    if (GUILayout.Button("Remove Condition"))
                    {
                        extraConditions.RemoveAt(i);
                        i--;
                    }
                    EditorGUILayout.EndVertical();
                }
                if (GUILayout.Button("Add Extra Condition"))
                {
                    extraConditions.Add(new ParameterCondition());
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Animator Controller not found.", MessageType.Info);
            }

            EditorGUILayout.Space();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("animationClipsList"), true);
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.EndScrollView();
        }
        else
        {
            animationsDirectory = EditorGUILayout.ObjectField("Animations Directory", animationsDirectory, typeof(DefaultAsset), false) as DefaultAsset;
            if (controller != null)
            {
                var intParams = controller.parameters.Where(p => p.type == AnimatorControllerParameterType.Int).Select(p => p.name).ToArray();
                if (intParams.Length > 0)
                {
                    int currentIndex = System.Array.IndexOf(intParams, selectedIntParameter);
                    if (currentIndex < 0) currentIndex = 0;
                    currentIndex = EditorGUILayout.Popup("Integer Parameter", currentIndex, intParams);
                    selectedIntParameter = intParams[currentIndex];
                }
                else
                {
                    selectedIntParameter = null;
                    EditorGUILayout.HelpBox("No integer parameters found in the animator controller.", MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Animator Controller not found.", MessageType.Info);
            }
        }

        GUI.enabled =
            controller != null &&
            (!addToVRCMenu || (vrcMenu != null && vrcParameters != null && submenuName != null)) &&
            (!quickOutfits || (animationsDirectory != null &&
                AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(animationsDirectory)) &&
                !string.IsNullOrEmpty(selectedIntParameter)));

        if (GUILayout.Button("Add Layers"))
        {
            if (!quickOutfits && !ValidateParameters(controller, addToVRCMenu ? vrcParameters : null,
                animationClipsList.Where(clip => clip != null).Select(clip => (prefix + clip.name, false))))
                return;

            Undo.RecordObject(controller, "Add Bool Layers");
            if (addToVRCMenu)
            {
                Undo.RecordObject(vrcMenu, "Add Bool Layers");
                Undo.RecordObject(vrcParameters, "Add Bool Layers");
            }
            AddNewBoolLayers();
            EditorUtility.SetDirty(controller);
            if (addToVRCMenu)
            {
                EditorUtility.SetDirty(vrcMenu);
                EditorUtility.SetDirty(vrcParameters);
            }
            animationClipsList.Clear();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    private void AddNewBoolLayers()
    {
        if (!quickOutfits)
        {
            foreach (var animationClip in animationClipsList)
            {
                if (animationClip == null)
                    continue;

                AnimatorControllerLayer newLayer = controller.layers.FirstOrDefault(layer => layer.name == (prefix + animationClip.name));
                if (newLayer == null)
                {
                    newLayer = new AnimatorControllerLayer
                    {
                        name = prefix + animationClip.name,
                        stateMachine = new AnimatorStateMachine
                        {
                            name = animationClip.name,
                            hideFlags = HideFlags.HideInHierarchy
                        }
                    };
                    if (AssetDatabase.GetAssetPath(controller) != "")
                        AssetDatabase.AddObjectToAsset(newLayer.stateMachine, AssetDatabase.GetAssetPath(controller));
                    newLayer.defaultWeight = 1f;
                    controller.AddLayer(newLayer);
                }
                else
                {
                    foreach (ChildAnimatorState state in newLayer.stateMachine.states)
                        newLayer.stateMachine.RemoveState(state.state);
                }

                newLayer.stateMachine.entryPosition = new Vector3(490, 0);
                newLayer.stateMachine.exitPosition = new Vector3(490, 50);
                newLayer.stateMachine.anyStatePosition = new Vector3(50, 0);

                newLayer.stateMachine.AddState("Default", new Vector3(250, 0)).writeDefaultValues = true;

                string animationName = animationClip.name;
                AnimatorState animationState = newLayer.stateMachine.AddState(animationName, new Vector3(250, 50));
                animationState.motion = animationClip;
                animationState.writeDefaultValues = true;

                CreateAnimatorTransitions(newLayer.stateMachine, newLayer.name, extraConditions);

                if (addToVRCMenu)
                {
                    AddExpressionParameter(vrcParameters, prefix + animationClip.name, "bool");
                    AddControl(vrcMenu, submenuName, animationClip.name, prefix + animationClip.name, 1.0F);
                }

                Debug.Log($"Successfully added layer <color=#54e354>{newLayer.name}</color>");
            }
        }
        else
        {
            string dirPath = AssetDatabase.GetAssetPath(animationsDirectory);
            if (!Directory.Exists(dirPath))
            {
                Debug.LogError("Selected Animations Directory does not exist.");
                return;
            }

            var dirs = Directory.GetDirectories(dirPath);
            var validDirs = new List<(int index, string name, string path, List<AnimationClip> clips)>();
            var regex = new Regex(@"^(\d+)\. (.+)$");

            foreach (var dir in dirs)
            {
                string dirName = Path.GetFileName(dir);
                var match = regex.Match(dirName);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int index) && index <= 255)
                {
                    string name = match.Groups[2].Value;
                    var clips = Directory.GetFiles(dir, "*.anim", SearchOption.TopDirectoryOnly)
                        .Select(f => {
                            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(f.Replace('\\', '/'));
                            if (clip == null) Debug.LogWarning($"Failed to load animation clip at {f}");
                            return clip;
                        })
                        .Where(clip => clip != null)
                        .ToList();
                    if (clips.Count > 0)
                    {
                        validDirs.Add((index, name, dir, clips));
                        Debug.Log($"Directory '{name}' (index {index}) has {clips.Count} animation clips.");
                    }
                    else
                    {
                        Debug.LogWarning($"No animation clips found in directory '{name}' (index {index}). Skipping.");
                    }
                }
                else
                {
                    Debug.LogWarning($"Directory '{dirName}' does not match the required naming pattern (e.g., '0. Set 1') and will be skipped.");
                }
            }

            validDirs.Sort((a, b) => a.index.CompareTo(b.index));
            if (validDirs.Count == 0 || validDirs.Select(dir => dir.index).Distinct().Count() != validDirs.Count ||
                validDirs.Select(dir => dir.name).Distinct().Count() != validDirs.Count)
            {
                Debug.LogError("Quick Outfits needs at least one outfit and unique folder indices and names.");
                return;
            }

            var requested = validDirs.SelectMany(dir => dir.clips.Select(clip => ($"{dir.name}/{clip.name}", false)))
                .Append((selectedIntParameter, true));
            if (!ValidateParameters(controller, addToVRCMenu ? vrcParameters : null, requested))
                return;

            Debug.Log($"Total valid directories: {validDirs.Count}");

            if (addToVRCMenu)
            {
                AddExpressionParameter(vrcParameters, selectedIntParameter, "int");
                // Build outfit submenus in memory
                var outfitMenus = new List<(string name, VRCExpressionsMenu menu)>();
                string menuBasePath = Path.GetDirectoryName(AssetDatabase.GetAssetPath(vrcMenu)).Replace("\\", "/");
                string submenuFolder = AssetDatabase.IsValidFolder($"{menuBasePath}/Submenu") ? $"{menuBasePath}/Submenu" : menuBasePath;
                if (!AssetDatabase.IsValidFolder($"{submenuFolder}/{submenuName}"))
                {
                    AssetDatabase.CreateFolder(submenuFolder, submenuName);
                }
                string outfitSubmenuPath = $"{submenuFolder}/{submenuName}";

                foreach (var (index, name, path, clips) in validDirs)
                {
                    VRCExpressionsMenu outfitMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    outfitMenu.name = name;

                    // Add "Enable" toggle
                    if (index != 0)
                        outfitMenu.controls.Add(new VRCExpressionsMenu.Control()
                    {
                        name = "Enable",
                        type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter() { name = selectedIntParameter },
                        value = index
                    });

                    // Add toggles for each animation clip
                    foreach (var clip in clips)
                    {
                        string layerName = $"{name}/{clip.name}";
                        AddExpressionParameter(vrcParameters, layerName, "bool");
                        outfitMenu.controls.Add(new VRCExpressionsMenu.Control()
                        {
                            name = clip.name,
                            type = VRCExpressionsMenu.Control.ControlType.Toggle,
                            parameter = new VRCExpressionsMenu.Control.Parameter() { name = layerName },
                            value = 1.0F
                        });
                    }
                    Debug.Log($"Outfit menu for '{name}' has {outfitMenu.controls.Count} controls before paging");

                    // Create asset for outfitMenu
                    string assetPath = $"{outfitSubmenuPath}/{name}.asset";
                    AssetDatabase.CreateAsset(outfitMenu, AssetDatabase.GenerateUniqueAssetPath(assetPath));

                    // Page the outfit submenu if necessary
                    PageMenuIfNeeded(outfitMenu, name, outfitSubmenuPath);
                    Debug.Log($"Outfit menu for '{name}' has {outfitMenu.controls.Count} controls after paging");

                    outfitMenus.Add((name, outfitMenu));

                    EditorUtility.SetDirty(outfitMenu);
                }

                // Create root submenu
                VRCExpressionsMenu rootSubmenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                rootSubmenu.name = submenuName;

                string rootAssetPath = $"{submenuFolder}/{submenuName}.asset";
                AssetDatabase.CreateAsset(rootSubmenu, AssetDatabase.GenerateUniqueAssetPath(rootAssetPath));

                // Add controls for each outfit submenu
                foreach (var (name, menu) in outfitMenus)
                {
                    rootSubmenu.controls.Add(new VRCExpressionsMenu.Control()
                    {
                        name = name,
                        type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                        subMenu = menu
                    });
                }
                Debug.Log($"Added {rootSubmenu.controls.Count} controls to root submenu '{submenuName}' before paging");

                // Page the root submenu if necessary
                PageMenuIfNeeded(rootSubmenu, submenuName, submenuFolder);
                Debug.Log($"Root submenu '{submenuName}' has {rootSubmenu.controls.Count} controls after paging");

                // Add root submenu to main menu if not already present
                SetSubmenuControl(vrcMenu, rootSubmenu, submenuName);

                EditorUtility.SetDirty(rootSubmenu);
            }

            // Create animator layers (unchanged)
            foreach (var (index, name, path, clips) in validDirs)
            {
                string tempPrefix = $"{name}/";
                var tempExtra = new List<ParameterCondition>
                {
                    new ParameterCondition
                    {
                        parameterName = selectedIntParameter,
                        conditionMode = AnimatorConditionMode.Equals,
                        threshold = index
                    }
                };

                foreach (var animationClip in clips)
                {
                    string layerName = tempPrefix + animationClip.name;

                    AnimatorControllerLayer newLayer = controller.layers.FirstOrDefault(layer => layer.name == layerName);
                    if (newLayer == null)
                    {
                        newLayer = new AnimatorControllerLayer
                        {
                            name = layerName,
                            stateMachine = new AnimatorStateMachine
                            {
                                name = animationClip.name,
                                hideFlags = HideFlags.HideInHierarchy
                            }
                        };
                        if (AssetDatabase.GetAssetPath(controller) != "")
                            AssetDatabase.AddObjectToAsset(newLayer.stateMachine, AssetDatabase.GetAssetPath(controller));
                        newLayer.defaultWeight = 1f;
                        controller.AddLayer(newLayer);
                    }
                    else
                    {
                        foreach (ChildAnimatorState state in newLayer.stateMachine.states)
                            newLayer.stateMachine.RemoveState(state.state);
                    }

                    newLayer.stateMachine.entryPosition = new Vector3(490, 0);
                    newLayer.stateMachine.exitPosition = new Vector3(490, 50);
                    newLayer.stateMachine.anyStatePosition = new Vector3(50, 0);

                    newLayer.stateMachine.AddState("Default", new Vector3(250, 0)).writeDefaultValues = true;

                    AnimatorState animationState = newLayer.stateMachine.AddState(animationClip.name, new Vector3(250, 50));
                    animationState.motion = animationClip;
                    animationState.writeDefaultValues = true;

                    CreateAnimatorTransitions(newLayer.stateMachine, layerName, tempExtra);

                    Debug.Log($"Successfully added layer <color=#54e354>{newLayer.name}</color>");
                }
            }
        }
    }

    private void PageMenuIfNeeded(VRCExpressionsMenu menu, string menuName, string parentPath)
    {
        var queue = new Queue<VRCExpressionsMenu>();
        queue.Enqueue(menu);

        while (queue.Count > 0)
        {
            var currentMenu = queue.Dequeue();
            while (currentMenu.controls.Count > 8)
            {
                VRCExpressionsMenu newPage = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                int pageNum = 2; // Default to 2
                // Calculate page number based on existing pages
                var existingPages = currentMenu.controls.Where(c => c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.name.StartsWith("Page ")).ToList();
                if (existingPages.Count > 0)
                {
                    pageNum = existingPages.Count + 2; // Next number after the existing ones
                }
                newPage.name = $"{menuName.Replace("Page", "").Trim()}Page{pageNum}";
                string pagePath = $"{parentPath}/{newPage.name}.asset";
                AssetDatabase.CreateAsset(newPage, AssetDatabase.GenerateUniqueAssetPath(pagePath));

                // Compute moveCount: leave 7 controls + 1 page control = 8 total in currentMenu
                int moveCount = currentMenu.controls.Count - 7; // Leave 7 regular controls
                // Move the excess controls to newPage
                if (moveCount > 0)
                {
                    for (int i = 7; i < currentMenu.controls.Count; i++)
                    {
                        newPage.controls.Add(currentMenu.controls[i]);
                    }
                    currentMenu.controls.RemoveRange(7, moveCount);
                }

                // Add the page control to currentMenu (after removing, so it now has 7 regular + 1 page = 8)
                currentMenu.controls.Add(new VRCExpressionsMenu.Control()
                {
                    name = $"Page {pageNum}",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = newPage
                });

                // Queue newPage for further processing if it exceeds 8
                queue.Enqueue(newPage);
                EditorUtility.SetDirty(newPage);
            }
            EditorUtility.SetDirty(currentMenu);
        }
    }

    private void CreateAnimatorTransitions(AnimatorStateMachine layerStateMachine, string layerName, List<ParameterCondition> extraConditionsToUse)
    {
        AddControllerParameter(controller, layerName, "bool");

        ChildAnimatorState animationChildState = layerStateMachine.states.FirstOrDefault(state => state.state.name != "Default");
        if (animationChildState.state == null)
            return;

        AnimatorStateTransition defaultToAnimation = layerStateMachine.defaultState.AddTransition(animationChildState.state);
        defaultToAnimation.AddCondition(AnimatorConditionMode.If, 0, layerName);
        defaultToAnimation.hasExitTime = false;
        defaultToAnimation.duration = 0.0F;

        AnimatorStateTransition animationToDefaultBase = animationChildState.state.AddTransition(layerStateMachine.defaultState);
        animationToDefaultBase.AddCondition(AnimatorConditionMode.IfNot, 0, layerName);
        animationToDefaultBase.hasExitTime = false;
        animationToDefaultBase.duration = 0.0F;

        foreach (var cond in extraConditionsToUse)
        {
            if (!string.IsNullOrEmpty(cond.parameterName))
            {
                defaultToAnimation.AddCondition(cond.conditionMode, cond.threshold, cond.parameterName);
                AnimatorStateTransition extraTransition = animationChildState.state.AddTransition(layerStateMachine.defaultState);
                extraTransition.AddCondition(InvertCondition(cond.conditionMode), cond.threshold, cond.parameterName);
                extraTransition.hasExitTime = false;
                extraTransition.duration = 0.0F;
            }
        }
    }

    private AnimatorConditionMode InvertCondition(AnimatorConditionMode mode)
    {
        switch (mode)
        {
            case AnimatorConditionMode.If:
                return AnimatorConditionMode.IfNot;
            case AnimatorConditionMode.IfNot:
                return AnimatorConditionMode.If;
            case AnimatorConditionMode.Greater:
                return AnimatorConditionMode.Less;
            case AnimatorConditionMode.Less:
                return AnimatorConditionMode.Greater;
            case AnimatorConditionMode.Equals:
                return AnimatorConditionMode.NotEqual;
            case AnimatorConditionMode.NotEqual:
                return AnimatorConditionMode.Equals;
            default:
                return mode;
        }
    }
}
#endif
