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

/*
    Changes:
    Fixed script errors on avatar build
*/

public class AddIntLayer : EditorWindow
{
    // Avatar components
    private VRCAvatarDescriptor avatarDescriptor;
    private AnimatorController controller;
    private bool addToVRCMenu = false;
    private VRCExpressionsMenu vrcMenu;
    private VRCExpressionParameters vrcParameters;
    private string submenuName = "Int";

    // Controller components
    private string layerName = "Int";

    // Selecting animations
    [SerializeField]
    private List<AnimationClip> animationClipsList = new List<AnimationClip>();

    private Vector2 scrollPosition;

    [MenuItem("Toolbox/Create Int Layer", false, 2)]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow<AddIntLayer>("Create Int Layer");
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

        layerName = EditorGUILayout.TextField("Layer Name", layerName);

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

        EditorGUILayout.Space();
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("animationClipsList"), true);
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.EndScrollView();

        GUI.enabled =
            controller != null &&
            !string.IsNullOrWhiteSpace(layerName) &&
            (!addToVRCMenu || (vrcMenu != null && vrcParameters != null && submenuName != null));

        if (GUILayout.Button("Add Layer"))
        {
            if (!ValidateParameters(controller, addToVRCMenu ? vrcParameters : null, new[] { (layerName, true) }))
                return;

            var menuValues = new Dictionary<string, int>();
            for (int i = 0; addToVRCMenu && i < animationClipsList.Count; i++)
            {
                var clip = animationClipsList[i];
                if (clip != null && (i >= 255 || !menuValues.TryAdd(clip.name, i + 1)))
                {
                    Debug.LogError("Int menu clips need distinct names and values from 1 through 255.");
                    return;
                }
            }

            Undo.RecordObject(controller, "Add Int Layer");

            if (addToVRCMenu)
            {
                Undo.RecordObject(vrcMenu, "Add Int Layer");
                Undo.RecordObject(vrcParameters, "Add Int Layer");
            }

            AddNewIntLayer();
            if (addToVRCMenu)
                SyncIntControls(vrcMenu, submenuName, layerName, menuValues);
            EditorUtility.SetDirty(controller);

            if (addToVRCMenu)
            {
                EditorUtility.SetDirty(vrcMenu);
                EditorUtility.SetDirty(vrcParameters);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    private void AddNewIntLayer()
    {
        AnimatorControllerLayer newLayer = controller.layers.FirstOrDefault(layer => layer.name == layerName);
        if (newLayer == null)
        {
            // Create layer and set up state machine
            newLayer = new AnimatorControllerLayer
            {
                name = layerName,
                stateMachine = new AnimatorStateMachine
                {
                    name = layerName,
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
            Undo.RecordObject(newLayer.stateMachine, "Rebuild Int Layer");
            foreach (var transition in newLayer.stateMachine.anyStateTransitions)
                newLayer.stateMachine.RemoveAnyStateTransition(transition);
            foreach (ChildAnimatorState state in newLayer.stateMachine.states)
            {
                newLayer.stateMachine.RemoveState(state.state);
            }
        }

        newLayer.stateMachine.entryPosition = new Vector3(490, 0);
        newLayer.stateMachine.exitPosition = new Vector3(490, 50);
        newLayer.stateMachine.anyStatePosition = new Vector3(50, 0);

        AddControllerParameter(controller, layerName, "int");
        var defaultState = newLayer.stateMachine.AddState("0_Default", new Vector3(250, 0));
        newLayer.stateMachine.defaultState = defaultState;
        AddTransition(newLayer.stateMachine, defaultState, 0);

        // add animations to layer
        int animationIndex = 0;
        int pos = 50;
        foreach (var animationClip in animationClipsList)
        {
            if (animationClip != null)
            {
                string animationName = animationClip.name;
                string stateName = $"{animationIndex + 1}_{animationName}";
                AnimatorState newState = newLayer.stateMachine.AddState(stateName, new Vector3(250, pos));
                pos += 50;
                newState.motion = animationClip;
                AddTransition(newLayer.stateMachine, newState, animationIndex + 1);
            }
            animationIndex++;

        }

        if (addToVRCMenu)
        {
            AddExpressionParameter(vrcParameters, layerName, "int");

        }

        EditorUtility.SetDirty(controller);

        Debug.Log($"Successfully added layer <color=#54e354>" + layerName + $"</color>");
    }

    private void AddTransition(AnimatorStateMachine stateMachine, AnimatorState state, int value)
    {
        var transition = stateMachine.AddAnyStateTransition(state);
        transition.AddCondition(AnimatorConditionMode.Equals, value, layerName);
        transition.hasExitTime = false;
        transition.canTransitionToSelf = false;
        transition.duration = 0f;
    }
}
#endif
